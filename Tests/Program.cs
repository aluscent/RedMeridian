using RedMeridian.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

var rules = new GameRules(Path.Combine(AppContext.BaseDirectory, "Config"));
(string Name, Action Run)[] tests =
[
    ("Battery charge, discharge and capacity limits", BatteryLimits),
    ("Footprint ownership and map boundaries", FootprintOwnership),
    ("Area build rejects unaffordable orders without partial edits", AreaBuildIsAtomic),
    ("Starter colony produces resources and loses solar output during storms", StarterProduction),
    ("Observatory narrows forecasts and preserves the last reading when offline", WeatherObservatory),
    ("Spacecraft found bases, retain blocked cargo, and restore without duplicate delivery", SpaceLogistics),
    ("Recurring routes respect reserves and avoid counting inbound cargo twice", RecurringCargo),
    ("Research requires explicit activation and preserves legacy unlocks", ResearchActivation),
    ("Tester commands are gated by server configuration", TesterCommands),
    ("Current saves preserve map edits, upgrades, and shutdowns", CurrentSaveRoundTrip),
    ("Legacy saves preserve the original footprint and resources", LegacySave),
    ("Overlapping structures in a save are rejected", InvalidSave)
];

int failures = 0;
foreach (var test in tests)
{
    try
    {
        test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception error)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {test.Name}: {error}");
    }
}
return failures == 0 ? 0 : 1;

void BatteryLimits()
{
    var bank = new ProductionGroup(new Building { Storage = 12, ChargeRate = 5, DischargeRate = 3 });
    bank.Add(1);
    var grid = new PowerGrid();
    grid.Configure([bank]);
    grid.Settle(100, 0);
    Equal(5L, grid.Charge);
    Equal(3L, grid.Available);
    grid.Settle(0, 3);
    Equal(2L, grid.Charge);
    grid.Restore(100);
    Equal(12L, grid.Charge);
    bank.Remove(1);
    grid.Configure([bank]);
    Equal(0L, grid.Charge);
}

void FootprintOwnership()
{
    var surface = new Surface(rules, null);
    var shape = new Footprint(2, 2);
    Equal(false, Surface.Fits(Colony.Size - 1, shape));
    Equal(false, Surface.Fits(Colony.Size * (Colony.Size - 1), shape));
    Equal(true, surface.Open(0, shape));
    surface.Occupy(0, shape);
    Equal(0, surface.AnchorAt(Colony.Size + 1));
    Equal(false, surface.Open(1, shape));
    surface.Clear(0);
    Equal(-1, surface.AnchorAt(Colony.Size + 1));
    Equal(true, surface.Open(1, shape));
}

void AreaBuildIsAtomic()
{
    var solar = rules.Buildings.Single(b => b.Art == "solar");
    var empty = new Colony(rules, empty: true).Export();
    foreach (var (id, cost) in solar.Cost) empty.Materials![id] = cost;
    var colony = Colony.Restore(rules, empty);
    Reject(() => colony.EditArea(0, 1, solar.Id));
    Equal(0, colony.Export().Tiles!.Length);
    foreach (var (id, cost) in solar.Cost) Equal((long)cost, colony.Export().Materials![id]);
    Equal(solar.Id, colony.Build(0, solar.Id).Edit.Type);
}

void StarterProduction()
{
    var colony = new Colony(rules);
    var initial = colony.Export();
    colony.Step();
    Equal(1L, colony.Export().Tick);
    Equal(true, colony.Export().Materials!["ore"] > initial.Materials!["ore"]);

    var solar = rules.Buildings.Single(b => b.Art == "solar");
    var beforeStorm = initial with { Tiles = [[0, solar.Id]], Tick = rules.World.Storm.Start - 1 };
    var solarColony = Colony.Restore(rules, beforeStorm);
    using var calm = Status(solarColony);
    Equal((long)solar.Generation, calm.RootElement.GetProperty("supply").GetInt64());
    solarColony.Step();
    using var storm = Status(solarColony);
    Equal(true, storm.RootElement.GetProperty("storm").GetBoolean());
    Equal((long)solar.StormGeneration!.Value, storm.RootElement.GetProperty("supply").GetInt64());
}

void CurrentSaveRoundTrip()
{
    var colony = new Colony(rules, map: new(-42, 0, new() { [0] = "iron", [1] = null }));
    var mine = rules.Buildings.Single(b => b.Deposit == "iron");
    colony.Build(0, mine.Id);
    colony.Upgrade(0);
    colony.Toggle(0);
    colony.Step();
    var saved = colony.Export();
    var json = System.Text.Json.JsonSerializer.Serialize(saved);
    var restored = Colony.Restore(rules, System.Text.Json.JsonSerializer.Deserialize<SaveData>(json)).Export();
    Equal(saved.Tick, restored.Tick);
    Equal(saved.Map!.Seed, restored.Map!.Seed);
    Equal(0, restored.Map.Abundance);
    Equal("iron", restored.Map.Paint![0]);
    Equal<string?>(null, restored.Map.Paint[1]);
    Equal(new SavedStructure(0, Level: 2, Disabled: true), restored.Structures!.Single(s => s.Index == 0));
    foreach (var (id, amount) in saved.Materials!) Equal(amount, restored.Materials![id]);
}

void WeatherObservatory()
{
    var observatory = rules.Buildings.Single(b => b.ForecastsWeather);
    var solar = rules.Buildings.Single(b => b.Art == "solar");
    var colony = new Colony(rules, empty: true);
    colony.Build(0, solar.Id);
    colony.Build(1, observatory.Id);
    Equal<Dictionary<int,WeatherForecast>?>(null, colony.Export().WeatherForecasts);
    colony.Step();
    var early = colony.Export().WeatherForecasts![1];
    Equal((long)solar.StormGeneration!.Value, early.StormSolarOutput);
    Equal<long?>(null, early.BatteryTicks);
    var preStorm = Colony.Restore(rules, colony.Export() with { Tick = rules.World.Storm.Start - 2 });
    preStorm.Step();
    var latest = preStorm.Export().WeatherForecasts![1];
    Equal(1L, latest.StartsInMin);
    Equal(1L, latest.StartsInMax);
    Equal((long)(rules.World.Storm.End - rules.World.Storm.Start), latest.DurationMin);
    Equal(true, early.StartsInMax - early.StartsInMin > latest.StartsInMax - latest.StartsInMin);
    preStorm.Toggle(1);
    preStorm.Step();
    var restored = Colony.Restore(rules, preStorm.Export());
    Equal(latest, restored.Export().WeatherForecasts![1]);
    Equal(true, restored.Export().Tick > latest.ObservedAt);
    restored.Remove(1);
    Equal<Dictionary<int,WeatherForecast>?>(null, restored.Export().WeatherForecasts);
}

SpaceProgram LaunchReadyProgram()
{
    var initial = new Colony(rules, empty: true).Export();
    initial.Materials!["alloy"] = 500;
    initial.Materials["propellant"] = 500;
    var mars = Colony.Restore(rules, initial);
    int Id(string role) => rules.Buildings.Single(b => b.SpaceRole == role).Id;
    mars.Build(0, rules.Buildings.Single(b => b.Art == "solar").Id);
    mars.Build(1, Id("relay"));
    mars.Build(2, Id("assembly"));
    mars.Build(3, Id("terminal"));
    var program = new SpaceProgram(rules, mars);
    program.Step();
    program.Assemble(mars);
    return program;
}

void SpaceLogistics()
{
    const string orbitId = "mars-orbit-1";
    var program = LaunchReadyProgram();
    var mars = program.Mars;
    long before = mars.Stock("alloy");
    Reject(() => program.Dispatch(mars, 1, orbitId, new() { ["alloy"] = SpaceProgram.Payload + 1 }));
    Equal(before, mars.Stock("alloy"));
    program.Dispatch(mars, 1, orbitId, new() { ["alloy"] = 20, ["propellant"] = 10, ["food"] = 10, ["water"] = 10 });
    Equal(before - 20 - SpaceProgram.FoundingAlloy, mars.Stock("alloy"));
    Reject(() => program.Base(orbitId));
    program = SpaceProgram.Restore(rules, program.Export());
    for (int i = 0; i < 6; i++) program.Step();
    var orbit = program.Base(orbitId);
    Equal(program.Mars.Tick, orbit.Tick);
    Equal(20L, orbit.Stock("alloy"));
    Equal(16, orbit.Site.Size);
    Reject(() => orbit.Build(0, 1));
    int iron = rules.Buildings.Single(b => b.Deposit == "iron").Id;
    Reject(() => orbit.Build(510 * Colony.Size + 510, iron));
    int lab = rules.Buildings.Single(b => b.Art == "lab" && b.Outputs.ContainsKey("research")).Id;
    orbit.Build(510 * Colony.Size + 510, lab);
    program.Step();
    Equal(3L, orbit.Stock("research"));
    program.Dispatch(orbit, 1, "mars", new() { ["propellant"] = 1 });
    program.Mars.Toggle(3);
    for (int i = 0; i < 6; i++) program.Step();
    long fuel = program.Mars.Stock("propellant");
    Equal("mars", program.Export().Space!.Ships[0].Destination);
    program = SpaceProgram.Restore(rules, program.Export());
    program.Mars.Toggle(3);
    program.Step();
    Equal(fuel + 1, program.Mars.Stock("propellant"));
    program.Step();
    Equal(fuel + 1, program.Mars.Stock("propellant"));
    Equal<string?>(null, program.Export().Space!.Ships[0].Destination);
}

void RecurringCargo()
{
    const string orbitId = "mars-orbit-1";
    var program = LaunchReadyProgram();
    program.Dispatch(program.Mars, 1, orbitId, new() { ["propellant"] = 10 });
    for (int i = 0; i < 7; i++) program.Step();
    program.Assemble(program.Mars);
    program.Assemble(program.Mars);
    int reserve = (int)program.Mars.Stock("alloy") - 9;
    program.SetRoute(new(2, "mars", orbitId, "alloy", reserve, 9));
    program.SetRoute(new(3, "mars", orbitId, "alloy", 0, 9));
    program.Step();
    Equal((long)reserve, program.Mars.Stock("alloy"));
    Equal(9L, program.Export().Space!.Ships[1].Cargo!["alloy"]);
    Equal<string?>(null, program.Export().Space!.Ships[2].Destination);
    for (int i = 0; i < 7; i++) program.Step();
    Equal(9L, program.Base(orbitId).Stock("alloy"));
    Equal<string?>(null, program.Export().Space!.Ships[1].Destination);
}

void LegacySave()
{
    var legacy = new SaveData(1, [[0, 1]], [31, 19, 8, 7], 4, 12, null);
    var colony = Colony.Restore(rules, legacy);
    Equal((byte)1, colony.Widths[0]);
    Equal((byte)1, colony.Heights[0]);
    Equal(31L, colony.Export().Materials!["ore"]);
    Equal(12L, colony.Export().Tick);
}

void ResearchActivation()
{
    var initial = new Colony(rules, empty: true).Export();
    var milestone = rules.Research.First(item => item.Requires.Length == 0 && item.Points > 0 &&
        rules.Buildings.Any(b => b.Research == item.Id && b.Deposit is null));
    var building = rules.Buildings.First(b => b.Research == milestone.Id && b.Deposit is null);
    foreach (var (id, amount) in building.Cost) initial.Materials![id] = amount;
    var colony = Colony.Restore(rules, initial);
    Reject(() => colony.ActivateResearch(milestone.Id));
    long points = rules.Research.Max(item => item.Points);
    initial.Materials![rules.World.ResearchMaterial] = points;
    colony = Colony.Restore(rules, initial);
    Equal(0, colony.Export().ActivatedResearch!.Length);
    Reject(() => colony.Build(0, building.Id));
    var dependent = rules.Research.First(item => item.Requires.Length > 0);
    Reject(() => colony.ActivateResearch(dependent.Id));
    colony.ActivateResearch(milestone.Id);
    Reject(() => colony.ActivateResearch(milestone.Id));
    Equal(points, colony.Stock(rules.World.ResearchMaterial));
    var restored = Colony.Restore(rules, colony.Export());
    Equal(milestone.Id, restored.Export().ActivatedResearch!.Single());
    Equal(building.Id, restored.Build(0, building.Id).Edit.Type);
    var legacy = Colony.Restore(rules, initial with { ActivatedResearch = null });
    Equal(true, legacy.Export().ActivatedResearch!.Contains(milestone.Id));
}

void TesterCommands()
{
    static int? Code(IResult result) => ((IStatusCodeHttpResult)result).StatusCode;
    var normal = new ColonySession(rules, new ConfigurationBuilder().Build());
    Equal<int?>(200, Code(normal.Apply(new("new"))));
    Equal<int?>(400, Code(normal.Apply(new("stress"))));
    Equal<int?>(400, Code(normal.Apply(new("tester-grant", Revision: 1, Material: "alloy", Amount: 100))));
    Equal<int?>(400, Code(normal.Apply(new("tester-step", Amount: 2))));

    var developer = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string,string?> { ["developer"] = "true" }).Build();
    var tester = new ColonySession(rules, developer);
    Equal<int?>(200, Code(tester.Apply(new("new"))));
    Equal<int?>(200, Code(tester.Apply(new("tester-grant", Revision: 1, Material: "alloy", Amount: 100))));
    Equal<int?>(200, Code(tester.Apply(new("tester-step", Amount: 2))));
    using var status = System.Text.Json.JsonSerializer.SerializeToDocument(tester.Read(-1));
    Equal(true, status.RootElement.GetProperty("developerMode").GetBoolean());
    Equal(true, status.RootElement.GetProperty("paused").GetBoolean());
    Equal(2L, status.RootElement.GetProperty("tick").GetInt64());
    Equal<int?>(200, Code(tester.Apply(new("stress"))));
}

void InvalidSave()
{
    var save = new Colony(rules, empty: true).Export() with
    {
        Tiles = [[0, 1], [1, 1]],
        Structures = [new SavedStructure(0, Width: 2)]
    };
    Reject(() => Colony.Restore(rules, save));
}

static System.Text.Json.JsonDocument Status(Colony colony) =>
    System.Text.Json.JsonSerializer.SerializeToDocument(colony.Status(0, false, false, 1));

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new Exception($"Expected {expected}, got {actual}.");
}

static void Reject(Action action)
{
    try { action(); }
    catch (InvalidOperationException) { return; }
    throw new Exception("Expected the operation to be rejected.");
}
