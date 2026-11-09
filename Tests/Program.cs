using RedMeridian.Domain;

var rules = new GameRules(Path.Combine(AppContext.BaseDirectory, "Config"));
(string Name, Action Run)[] tests =
[
    ("Battery charge, discharge and capacity limits", BatteryLimits),
    ("Footprint ownership and map boundaries", FootprintOwnership),
    ("Area build rejects unaffordable orders without partial edits", AreaBuildIsAtomic),
    ("Starter colony produces resources and loses solar output during storms", StarterProduction),
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

void LegacySave()
{
    var legacy = new SaveData(1, [[0, 1]], [31, 19, 8, 7], 4, 12, null);
    var colony = Colony.Restore(rules, legacy);
    Equal((byte)1, colony.Widths[0]);
    Equal((byte)1, colony.Heights[0]);
    Equal(31L, colony.Export().Materials!["ore"]);
    Equal(12L, colony.Export().Tick);
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
