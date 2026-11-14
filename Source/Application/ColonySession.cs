using System.Text.Json;
using RedMeridian.Domain;

namespace RedMeridian.Application;

sealed class ColonySession
{
    readonly object gate = new();
    readonly SaveRepository saves;
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
        saves = new(developerMode);
        program = new(rules, new Colony(rules,site: rules.NewSite(ColonySite.Mars)));
    }

    Colony Current => stress ?? program.Base(selectedBase);
    object State(int inspect = -1) => Current.Status(revision,stress is not null,paused,speed,
        started,saves.HasDefault,worldRevision,inspect,developerMode,
        stress is null && inspect >= 0 ? program.InspectTelescope(Current,inspect) : null,
        program.VisitedGalaxies);

    public object Saves()
    {
        lock (gate) return saves.List();
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
                        string loadPath = saves.PathFor(command.SaveSlot);
                        if (!File.Exists(loadPath)) throw new InvalidOperationException("No saved colony on disk.");
                        var saved = saves.Read(command.SaveSlot);
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
                    case "toggle":
                    case "upgrade":
                    case "area":
                        var construction = ConstructionCommands.Apply(Current,command,revision);
                        (message,edits) = (construction.Message,construction.Edits);
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
                        var research = ResearchCommands.Activate(Current,command,revision);
                        (message,edits) = (research.Message,research.Edits);
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
                            (string.IsNullOrEmpty(command.SaveSlot) || File.Exists(saves.PathFor(slot))))
                            throw new InvalidOperationException("Choose a new, unused save name.");
                        saves.Write(program,slot,command.SaveSlot is null ? diskVersion : 0);
                        activeSlot = slot;
                        diskVersion = Colony.SaveVersion;
                        message = slot is null or "" ? "Colony saved to disk." : $"Colony saved as {slot}.";
                        break;
                    case "import":
                        if (stress is not null) throw new InvalidOperationException("Return to your colony to import.");
                        if (command.Revision != revision) throw new InvalidOperationException("Map changed. Try importing again.");
                        var imported = SpaceProgram.Restore(rules,command.Save);
                        saves.Write(imported,null,0);
                        activeSlot = null;
                        diskVersion = Colony.SaveVersion;
                        Open(imported);
                        message = "Colony imported and saved.";
                        break;
                    case "select-base":
                        if (stress is not null) throw new InvalidOperationException("Return to your colony to use logistics.");
                        if (command.Revision != revision) throw new InvalidOperationException("Base changed. Try again.");
                        selectedBase = program.Base(command.Base).Site.Id;
                        worldRevision++;
                        message = $"Viewing {Current.Site.Name}.";
                        revision++;
                        break;
                    case "assemble":
                    case "dispatch":
                    case "explore":
                    case "set-route":
                    case "clear-route":
                        if (stress is not null) throw new InvalidOperationException("Return to your colony to use logistics.");
                        var fleet = FleetCommands.Apply(program,Current,command,revision);
                        (message,edits) = (fleet.Message,fleet.Edits);
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

}


