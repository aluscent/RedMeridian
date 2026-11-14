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
app.MapGet("/api/catalog", (GameRules rules) => new { rules.Buildings, rules.Materials, rules.Research, rules.World, rules.ShipTypes,
    landingSite = rules.NewSite(ColonySite.Mars) });
app.MapGet("/api/saves", (ColonySession session) => session.Saves());
app.MapGet("/api/state", (int? inspect, ColonySession session) => session.Read(inspect ?? -1));
app.MapGet("/api/surface", (HttpContext context, ColonySession session) => session.Surface(context));
app.MapGet("/api/map", (HttpContext context, ColonySession session) => session.Map(context));
app.MapGet("/api/map-resources", (string mapId, ColonySession session) => session.MapResources(mapId));
app.MapGet("/api/climate", (HttpContext context, ColonySession session) => session.Climate(context));
app.MapGet("/api/logistics", (ColonySession session) => session.Logistics());
app.MapPost("/api/command", (Command command, ColonySession session) => session.Apply(command));
app.MapPost("/api/import", ImportColony);
using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
var ticks = TickLoop(app.Services.GetRequiredService<ColonySession>(), app.Lifetime.ApplicationStopping);
await app.RunAsync();
await ticks;

static async Task<IResult> ImportColony(HttpContext context, int revision, ColonySession session)
{
    var limit = context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
    if (limit is { IsReadOnly: false }) limit.MaxRequestBodySize = SaveCodec.MaxUploadBytes;
    if (context.Request.ContentLength > SaveCodec.MaxUploadBytes)
        return Results.BadRequest(new { message = "Save file exceeds 64 MiB." });
    try
    {
        using var file = new MemoryStream();
        var buffer = new byte[64 * 1024];
        int count;
        while ((count = await context.Request.Body.ReadAsync(buffer,context.RequestAborted)) != 0)
        {
            if (file.Length + count > SaveCodec.MaxUploadBytes)
                return Results.BadRequest(new { message = "Save file exceeds 64 MiB." });
            file.Write(buffer,0,count);
        }
        file.Position = 0;
        var save = SaveCodec.Read(file,limitExpandedSize: true);
        return session.Apply(new("import",Revision: revision,Save: save));
    }
    catch (BadHttpRequestException error) when (error.StatusCode == StatusCodes.Status413PayloadTooLarge)
    {
        return Results.BadRequest(new { message = "Save file exceeds 64 MiB." });
    }
    catch (Exception error) when (error is JsonException or InvalidDataException)
    {
        return Results.BadRequest(new { message = "Invalid colony save. " + error.Message });
    }
}

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
    string? Material = null, int Amount = 0, string? ShipType = null, ClimateSettings? Climate = null, string? SaveSlot = null,
    bool GalaxySurvey = false, string[]? Materials = null);


sealed class ColonySession
{
    readonly object gate = new();
    readonly string defaultSave;
    readonly string saveDirectory;
    string? activeSlot;
    readonly bool developerMode;
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
        defaultSave = Path.GetFullPath(Environment.GetEnvironmentVariable("RED_MERIDIAN_SAVE")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RedMeridian", developerMode ? "colony.test.json" : "colony.json"));
        saveDirectory = defaultSave + ".slots";
        program = new(rules, new Colony(rules,site: rules.NewSite(ColonySite.Mars)));
    }

    Colony Current => stress ?? program.Base(selectedBase);
    object State(int inspect = -1) => Current.Status(revision,stress is not null,paused,speed,
        started,File.Exists(defaultSave),worldRevision,inspect,developerMode,
        stress is null && inspect >= 0 ? program.InspectTelescope(Current,inspect) : null,
        program.VisitedGalaxies);

    string SavePath(string? slot)
    {
        if (string.IsNullOrEmpty(slot)) return defaultSave;
        if (slot.Length > 48 || slot != slot.Trim() ||
            !slot.All(c => char.IsAsciiLetterOrDigit(c) || c is ' ' or '-' or '_'))
            throw new InvalidOperationException("Use 1-48 letters, numbers, spaces, hyphens, or underscores for the save name.");
        return Path.Combine(saveDirectory,"slot-" + slot + ".json");
    }

    public object Saves()
    {
        lock (gate)
        {
            var slots = new List<object>();
            if (File.Exists(defaultSave))
                slots.Add(new { id = "", name = "Default colony", updated = File.GetLastWriteTimeUtc(defaultSave) });
            if (Directory.Exists(saveDirectory))
                foreach (var file in Directory.EnumerateFiles(saveDirectory,"slot-*.json").OrderByDescending(File.GetLastWriteTimeUtc))
                {
                    var name = Path.GetFileNameWithoutExtension(file)[5..];
                    slots.Add(new { id = name, name, updated = File.GetLastWriteTimeUtc(file) });
                }
            return slots;
        }
    }

    public object Read(int inspect) { lock (gate) return State(inspect); }
    public MapSettings Surface(HttpContext context)
    {
        lock (gate)
        {
            context.Response.Headers["X-World-Revision"] = worldRevision.ToString();
            context.Response.Headers.CacheControl = "no-store";
            return Current.MapSettings;
        }
    }
    public object Logistics() { lock (gate) return program.Summary(selectedBase); }
    public IResult MapResources(string mapId)
    {
        lock (gate)
        {
            var colony = program.FindMap(mapId);
            return colony is null ? Results.NotFound() : Results.Ok(colony.ResourceSnapshot());
        }
    }
    public IResult Climate(HttpContext context)
    {
        lock (gate)
        {
            context.Response.Headers["X-World-Revision"] = worldRevision.ToString();
            context.Response.Headers.CacheControl = "no-store";
            return Results.Bytes(Current.ClimateMap(),"application/octet-stream");
        }
    }

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
            context.Response.Headers["X-World-Revision"] = worldRevision.ToString();
            context.Response.Headers.CacheControl = "no-store";
            return Results.Bytes(Current.MapLayout(), "application/octet-stream",
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
                        Open(SpaceProgram.StartCampaign(rules,command.Map));
                        activeSlot = null;
                        diskVersion = 0;
                        message = "Landed on the generated map.";
                        break;
                    case "climate":
                        if (command.Revision != revision || command.Climate is null)
                            throw new InvalidOperationException("Map changed or no climate settings supplied.");
                        int flooded = Current.ChangeClimate(command.Climate);
                        worldRevision++;
                        revision++;
                        message = flooded == 0 ? "Map climate updated." : $"Flooding demolished {flooded} structures.";
                        break;
                    case "load":
                        string loadPath = SavePath(command.SaveSlot);
                        if (!File.Exists(loadPath)) throw new InvalidOperationException("No saved colony on disk.");
                        var saved = ReadSave(loadPath);
                        Open(SpaceProgram.Restore(rules,saved));
                        activeSlot = command.SaveSlot;
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
                        string[] materials = command.Materials ?? (command.Material is { } material ? [material] : []);
                        if (command.Materials is not null && command.Material is not null ||
                            materials.Length == 0 || materials.Length > rules.Materials.Length ||
                            materials.Distinct().Count() != materials.Length || command.Amount is < 1 or > 100_000 ||
                            materials.Any(id => !program.MaterialAvailable(id)))
                            throw new InvalidOperationException("Select available resources and enter an amount from 1 to 100,000.");
                        var grant = materials.ToDictionary(id => id, _ => (long)command.Amount);
                        if (!Current.CanReceive(grant)) throw new InvalidOperationException("Grant exceeds storage capacity.");
                        Current.Receive(grant);
                        message = materials.Length == 1 ? $"Added {command.Amount:N0} units of {materials[0]}." :
                            $"Added {command.Amount:N0} units each to {materials.Length} selected resources.";
                        break;
                    case "tester-unlock-research":
                        RequireDeveloperMode();
                        if (command.Revision != revision) throw new InvalidOperationException("Base changed. Try again.");
                        int unlocked = Current.UnlockAllResearch();
                        message = unlocked == 0 ? "All available research is already active." : $"Unlocked {unlocked} research milestones in this galaxy.";
                        edits = [];
                        revision++;
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
                        string? slot = command.SaveSlot ?? activeSlot;
                        if (command.SaveSlot is not null &&
                            (string.IsNullOrEmpty(command.SaveSlot) || File.Exists(SavePath(slot))))
                            throw new InvalidOperationException("Choose a new, unused save name.");
                        Persist(program,slot,command.SaveSlot is null ? diskVersion : 0);
                        activeSlot = slot;
                        diskVersion = Colony.SaveVersion;
                        message = slot is null or "" ? "Colony saved to disk." : $"Colony saved as {slot}.";
                        break;
                    case "import":
                        if (stress is not null) throw new InvalidOperationException("Return to your colony to import.");
                        if (command.Revision != revision) throw new InvalidOperationException("Map changed. Try importing again.");
                        var imported = SpaceProgram.Restore(rules,command.Save);
                        Persist(imported,null,0);
                        activeSlot = null;
                        diskVersion = Colony.SaveVersion;
                        Open(imported);
                        message = "Colony imported and saved.";
                        break;
                    case "select-base":
                    case "assemble":
                    case "dispatch":
                    case "explore":
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
            catch (Exception error) when (error is InvalidOperationException or JsonException or InvalidDataException)
            {
                return Results.BadRequest(new { message = error.Message, state = State() });
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine(error);
                return Results.Json(new { message = "Could not write the colony save.", state = State() }, statusCode: 500);
            }
            if (command.Action is "remove" or "area" or "climate") program.PruneSurveys();
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
                program.Assemble(Current, command.Index, command.ShipType);
                return "Spacecraft assembly started.";
            case "dispatch":
                program.Dispatch(Current, command.Ship, command.Base, command.Cargo);
                return "Spacecraft launched.";
            case "explore":
                program.StartSurvey(Current,command.Index,command.GalaxySurvey);
                return "Exploration started.";
            case "set-route":
                program.SetRoute(command.Route ?? throw new InvalidOperationException("Provide a cargo route."));
                return "Recurring cargo route configured.";
            default:
                program.ClearRoute(command.Ship);
                return "Recurring route cleared.";
        }
    }

    void Persist(SpaceProgram colony,string? slot,int existingVersion)
    {
        var fullPath = SavePath(slot);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporary = fullPath + ".tmp";
        try
        {
            using (var output = File.Create(temporary)) SaveCodec.Write(output,colony.Export());
            if (File.Exists(fullPath))
            {
                if (existingVersion == 0) existingVersion = ReadSave(fullPath)?.Version ?? 0;
                string backup = fullPath + (existingVersion < 3 ? ".pre-v3" : existingVersion == 3 ? ".pre-v4" : ".pre-v5");
                if (existingVersion is > 0 and < Colony.SaveVersion && !File.Exists(backup)) File.Copy(fullPath,backup);
            }
            File.Move(temporary,fullPath,overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    static SaveData? ReadSave(string path)
    {
        using var input = File.OpenRead(path);
        return SaveCodec.Read(input);
    }
}


