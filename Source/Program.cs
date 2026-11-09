using System.Diagnostics;
using System.Text.Json;
using RedMeridian.Domain;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(builder.Configuration["urls"] ?? "http://127.0.0.1:5274");
builder.Services.AddSingleton(new GameRules(Path.Combine(builder.Environment.ContentRootPath, "Config")));
builder.Services.AddSingleton<ColonySession>();
var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = "no-cache"
});
app.MapGet("/api/catalog", (GameRules rules) => new { rules.Buildings, rules.Materials, rules.Research, rules.World });
app.MapGet("/api/state", (int? inspect, ColonySession session) => session.Read(inspect ?? -1));
app.MapGet("/api/surface", (ColonySession session) => session.Surface());
app.MapGet("/api/map", (HttpContext context, ColonySession session) => session.Map(context));
app.MapGet("/api/logistics", (ColonySession session) => session.Logistics());
app.MapPost("/api/command", (Command command, ColonySession session) => session.Apply(command));
using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
var ticks = TickLoop(app.Services.GetRequiredService<ColonySession>(), app.Lifetime.ApplicationStopping);
await app.RunAsync();
await ticks;

async Task TickLoop(ColonySession session, CancellationToken cancellation)
{
    try
    {
        var elapsed = Stopwatch.StartNew();
        while (await timer.WaitForNextTickAsync(cancellation))
        {
            session.Advance(elapsed.Elapsed.TotalSeconds);
            elapsed.Restart();
        }
    }
    catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
}

record Command(string? Action, int Index = -1, int Type = 0, int Revision = -1, SaveData? Save = null,
    int EndIndex = -1, MapSettings? Map = null, string? Base = null, int Ship = 0,
    Dictionary<string,long>? Cargo = null, CargoRoute? Route = null, string? Technology = null,
    string? Material = null, int Amount = 0);


sealed class ColonySession
{
    readonly object gate = new();
    readonly string path;
    readonly bool developerMode;
    readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web);
    readonly GameRules rules;
    int diskVersion;
    bool started;
    SpaceProgram program;
    string selectedBase = ColonySite.Mars.Id;
    Colony? stress;
    bool paused;
    int speed = 1, revision, worldRevision;
    double progress;

    public ColonySession(GameRules rules, IConfiguration configuration)
    {
        this.rules = rules;
        developerMode = string.Equals(configuration["developer"], "true", StringComparison.OrdinalIgnoreCase);
        path = Environment.GetEnvironmentVariable("RED_MERIDIAN_SAVE")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RedMeridian", developerMode ? "colony.test.json" : "colony.json");
        program = new(rules, new Colony(rules));
    }

    Colony Current => stress ?? program.Base(selectedBase);
    object State(int inspect = -1) => Current.Status(revision,stress is not null,paused,speed,
        started,File.Exists(path),worldRevision,inspect,developerMode);

    public object Read(int inspect) { lock (gate) return State(inspect); }
    public MapSettings Surface() { lock (gate) return Current.MapSettings; }
    public object Logistics() { lock (gate) return program.Summary(); }

    void Open(SpaceProgram loaded)
    {
        program = loaded;
        selectedBase = ColonySite.Mars.Id;
        stress = null;
        started = true;
        paused = false;
        speed = 1;
        progress = 0;
        worldRevision++;
        revision++;
    }

    public IResult Map(HttpContext context)
    {
        lock (gate)
        {
            context.Response.Headers["X-Map-Revision"] = revision.ToString();
            context.Response.Headers.CacheControl = "no-store";
            int size = Current.Grid.Length;
            var layout = new byte[size * 3];
            Current.Grid.CopyTo(layout,0);
            Current.Widths.CopyTo(layout,size);
            Current.Heights.CopyTo(layout,2 * size);
            return Results.Bytes(layout, "application/octet-stream",
                lastModified: null, entityTag: null, enableRangeProcessing: false);
        }
    }

    public void Advance(double seconds)
    {
        lock (gate)
        {
            if (paused || !started) return;
            progress += Math.Min(seconds, 2) * speed;
            while (progress >= 2)
            {
                StepSimulation();
                progress -= 2;
            }
        }
    }

    void StepSimulation()
    {
        if (stress is not null) stress.Step();
        else program.Step();
    }

    public IResult Apply(Command command)
    {
        lock (gate)
        {
            string message;
            TileEdit[]? edits = null;
            try
            {
                if (!started && command.Action is not ("new" or "load" or "import"))
                    throw new InvalidOperationException("Start or load a colony first.");
                switch (command.Action)
                {
                    case "new":
                        Open(new(rules, new Colony(rules,map: command.Map)));
                        message = "Landed on the generated map.";
                        break;
                    case "load":
                        if (!File.Exists(path)) throw new InvalidOperationException("No saved colony on disk.");
                        var saved = JsonSerializer.Deserialize<SaveData>(File.ReadAllText(path),json);
                        Open(SpaceProgram.Restore(rules,saved));
                        diskVersion = saved!.Version;
                        message = "Saved colony loaded.";
                        break;
                    case "menu":
                        started = false;
                        stress = null;
                        worldRevision++;
                        revision++;
                        message = "Choose a map or load a save.";
                        break;
                    case "build":
                    case "remove":
                        if (command.Revision != revision) throw new InvalidOperationException("Map changed. Refreshing the sector.");
                        var change = command.Action == "build"
                            ? Current.Build(command.Index, command.Type)
                            : Current.Remove(command.Index);
                        message = change.Message;
                        edits = [change.Edit];
                        revision++;
                        break;
                    case "toggle":
                    case "upgrade":
                        if (command.Revision != revision) throw new InvalidOperationException("Map changed. Refreshing the sector.");
                        message = command.Action == "toggle" ? Current.Toggle(command.Index) : Current.Upgrade(command.Index);
                        edits = [];
                        revision++;
                        break;
                    case "area":
                        if (command.Revision != revision) throw new InvalidOperationException("Map changed. Refreshing the sector.");
                        (message, edits) = Current.EditArea(command.Index, command.EndIndex, command.Type);
                        revision++;
                        break;
                    case "pause":
                        paused = !paused;
                        message = paused ? "Simulation paused." : "Simulation resumed.";
                        break;
                    case "speed":
                        speed = speed == 4 ? 1 : speed * 2;
                        message = $"Simulation speed {speed}×.";
                        break;
                    case "stress":
                        RequireDeveloperMode();
                        stress = stress is null ? new Colony(rules,true) : null;
                        worldRevision++;
                        revision++;
                        progress = 0;
                        message = stress is null ? "Returned to your colony." : "Loaded 100,000 structures in a separate colony.";
                        break;
                    case "activate-research":
                        if (command.Revision != revision) throw new InvalidOperationException("Base changed. Try again.");
                        message = Current.ActivateResearch(command.Technology);
                        edits = [];
                        revision++;
                        break;
                    case "tester-grant":
                        RequireDeveloperMode();
                        if (command.Revision != revision) throw new InvalidOperationException("Base changed. Try again.");
                        if (command.Material is not { } material || command.Amount is < 1 or > 100_000 ||
                            !rules.Materials.Any(item => item.Id == material))
                            throw new InvalidOperationException("Select a resource and an amount from 1 to 100,000.");
                        var grant = new Dictionary<string,long> { [material] = command.Amount };
                        if (!Current.CanReceive(grant)) throw new InvalidOperationException("Grant exceeds storage capacity.");
                        Current.Receive(grant);
                        message = $"Added {command.Amount} {material} to this base.";
                        break;
                    case "tester-step":
                        RequireDeveloperMode();
                        if (command.Amount is < 1 or > 100)
                            throw new InvalidOperationException("Choose between 1 and 100 ticks.");
                        paused = true;
                        progress = 0;
                        for (int i = 0; i < command.Amount; i++) StepSimulation();
                        message = $"Advanced {command.Amount} ticks. Simulation paused.";
                        break;
                    case "save":
                        if (stress is not null) throw new InvalidOperationException("Return to your colony to save.");
                        Persist(program);
                        message = "Colony saved to disk.";
                        break;
                    case "import":
                        if (stress is not null) throw new InvalidOperationException("Return to your colony to import.");
                        if (command.Revision != revision) throw new InvalidOperationException("Map changed. Try importing again.");
                        var imported = SpaceProgram.Restore(rules,command.Save);
                        Persist(imported);
                        Open(imported);
                        message = "Colony imported and saved.";
                        break;
                    case "select-base":
                    case "assemble":
                    case "dispatch":
                    case "set-route":
                    case "clear-route":
                        if (stress is not null) throw new InvalidOperationException("Return to your colony to use logistics.");
                        if (command.Revision != revision) throw new InvalidOperationException("Base changed. Try again.");
                        if (command.Action == "select-base")
                        {
                            selectedBase = program.Base(command.Base).Site.Id;
                            worldRevision++;
                            message = $"Viewing {Current.Site.Name}.";
                        }
                        else
                        {
                            message = ApplyLogistics(command);
                            edits = [];
                        }
                        revision++;
                        break;
                    default:
                        throw new InvalidOperationException("Unknown command.");
                }
            }
            catch (Exception error) when (error is InvalidOperationException or JsonException)
            {
                return Results.BadRequest(new { message = error.Message, state = State() });
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine(error);
                return Results.Json(new { message = "Could not write the colony save.", state = State() }, statusCode: 500);
            }
            return Results.Ok(new { message, edits, state = State() });
        }
    }

    void RequireDeveloperMode()
    {
        if (!developerMode) throw new InvalidOperationException("This command requires developer mode.");
    }

    string ApplyLogistics(Command command)
    {
        switch (command.Action)
        {
            case "assemble":
                program.Assemble(Current);
                return "Spacecraft assembled.";
            case "dispatch":
                program.Dispatch(Current, command.Ship, command.Base, command.Cargo);
                return "Spacecraft launched.";
            case "set-route":
                program.SetRoute(command.Route ?? throw new InvalidOperationException("Provide a cargo route."));
                return "Recurring cargo route configured.";
            default:
                program.ClearRoute(command.Ship);
                return "Recurring route cleared.";
        }
    }

    void Persist(SpaceProgram colony)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporary = fullPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(colony.Export(), json));
        if (File.Exists(fullPath))
        {
            if (diskVersion == 0)
                diskVersion = JsonSerializer.Deserialize<SaveData>(File.ReadAllText(fullPath),json)?.Version ?? 0;
            string backup = fullPath + (diskVersion < 3 ? ".pre-v3" : ".pre-v4");
            if (diskVersion is > 0 and < 4 && !File.Exists(backup)) File.Copy(fullPath,backup);
        }
        File.Move(temporary, fullPath, overwrite: true);
        diskVersion = 4;
    }
}


