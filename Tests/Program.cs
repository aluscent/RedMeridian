using RedMeridian.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

var rules = new GameRules(Path.Combine(AppContext.BaseDirectory, "Config"));
(string Name, Action Run)[] tests =
[
    ("New planet bounds vary and old colonies preserve full maps", PlanetBounds),
    ("Battery charge, discharge and capacity limits", BatteryLimits),
    ("Footprint ownership and map boundaries", FootprintOwnership),
    ("Area build rejects unaffordable orders without partial edits", AreaBuildIsAtomic),
    ("Starter colony produces resources and loses solar output during storms", StarterProduction),
    ("Observatory narrows forecasts and preserves the last reading when offline", WeatherObservatory),
    ("Spacecraft found bases, retain blocked cargo, and restore without duplicate delivery", SpaceLogistics),
    ("Recurring routes respect reserves and avoid counting inbound cargo twice", RecurringCargo),
    ("Assembly pauses offline and preserves typed orders through saves", TimedAssembly),
    ("Telescope surveys spend research, pause offline, and unlock systems and galaxies on schedule", TelescopeDiscovery),
    ("Ship ranges, travel time, and galaxy research apply across discovered systems", IntergalacticTravel),
    ("Seeded campaigns vary while respecting exploration tiers and viable landing inputs", GeneratedUniverses),
    ("Generated discoveries, flight quotes and landed maps survive saves", GeneratedCampaignRoundTrip),
    ("Planetary engineering requires long-term investment and runs only on planets", PlanetaryEngineering),
    ("Meridian terrain has bounded slopes and rejects steep footprints", MeridianTerrain),
    ("Powered housing provides beds without workers", UnstaffedHousing),
    ("Mars migration accrues fractional growth and staffed facilities add colonists", PopulationGrowth),
    ("Climate is seeded and humidity narrows both sides of the temperature band", ClimateMath),
    ("Ocean transitions remove whole footprints once and preserve usable shore", OceanTransitions),
    ("Climate controls solar output and suspends and restores extractors", ClimateOperation),
    ("New maps enforce deposit density bounds and legacy saves retain generation settings", MapDensity),
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

void PlanetBounds()
{
    var mars = rules.NewSite(ColonySite.Mars);
    var venus = rules.NewSite(ColonySite.All.Single(site => site.Id == "venus"));
    Equal(10_000, mars.Width * mars.Height);
    Equal(13_440, venus.Width * venus.Height);
    var colony = new Colony(rules, site: mars);
    int last = (mars.Top + mars.Height - 1) * Colony.Size + mars.Left + mars.Width - 1;
    var solar = rules.Buildings.Single(building => building.Art == "solar");
    colony.Build(last, solar.Id);
    Reject(() => colony.Build(last + 1, solar.Id));
    var restored = Colony.Restore(rules, colony.Export());
    Equal(mars.Width, restored.Site.Width);
    Equal((byte)solar.Id, restored.Grid[last]);

    var oldColony = new Colony(rules);
    oldColony.Build(0, solar.Id);
    var oldRestored = Colony.Restore(rules, oldColony.Export());
    Equal(Colony.Size, oldRestored.Site.Width);
    Equal((byte)solar.Id, oldRestored.Grid[0]);
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
    colony.Receive(new() { ["alloy"] = 100 });
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
    colony.Receive(new() { ["alloy"] = 100 });
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
    AssembleShuttle(program);
    return program;
}

void AssembleShuttle(SpaceProgram program)
{
    var type = rules.ShipType(GameRules.LegacyShipType);
    program.Assemble(program.Mars, 2, type.Id);
    for (int i = 0; i < type.BuildTicks; i++) program.Step();
}

void SpaceLogistics()
{
    const string orbitId = "mars-orbit-1";
    var program = LaunchReadyProgram();
    var mars = program.Mars;
    long before = mars.Stock("alloy");
    Reject(() => program.Dispatch(mars, 1, orbitId, new() { ["alloy"] = rules.ShipType(GameRules.LegacyShipType).Payload + 1 }));
    Equal(before, mars.Stock("alloy"));
    program.Dispatch(mars, 1, orbitId, new() { ["alloy"] = 20, ["propellant"] = 10, ["food"] = 10, ["water"] = 10 });
    Equal(before - 20 - SpaceProgram.FoundingAlloy, mars.Stock("alloy"));
    Reject(() => program.Base(orbitId));
    program = SpaceProgram.Restore(rules, program.Export());
    for (int i = 0; i < 6; i++) program.Step();
    var orbit = program.Base(orbitId);
    Equal(program.Mars.Tick, orbit.Tick);
    Equal(20L, orbit.Stock("alloy"));
    Equal(rules.Buildings.Single(b => b.SpaceRole == "hub").Id, orbit.Export().Tiles!.Single()[1]);
    Equal(true, orbit.CanDock);
    Equal(5, orbit.Site.Width);
    Equal(150, orbit.Site.Height);
    var legacy = program.Export();
    legacy.Space!.Bases[orbitId] = legacy.Space.Bases[orbitId] with { LongOrbit = false };
    Equal(16, SpaceProgram.Restore(rules, legacy).Base(orbitId).Site.Width);
    string research = rules.World.ResearchMaterial;
    var milestone = rules.Research.First(item => item.Requires.Length == 0);
    orbit.Receive(new() { [research] = milestone.Points });
    orbit.ActivateResearch(milestone.Id);
    Equal(true, program.Mars.Export().ActivatedResearch!.Contains(milestone.Id));
    Equal(true, SpaceProgram.Restore(rules, program.Export()).Mars.Export().ActivatedResearch!.Contains(milestone.Id));
    Reject(() => orbit.Build(0, 1));
    int iron = rules.Buildings.Single(b => b.Deposit == "iron").Id;
    Reject(() => orbit.Build(510 * Colony.Size + 510, iron));
    int lab = rules.Buildings.Single(b => b.Galaxy is null && b.Art == "lab" && b.Outputs.ContainsKey("research")).Id;
    orbit.Receive(new() { ["alloy"] = 200 });
    orbit.Build(510 * Colony.Size + 511, 1);
    orbit.Build(510 * Colony.Size + 512, rules.Buildings.Single(b => b.SpaceRole == "relay").Id);
    orbit.Build(510 * Colony.Size + 510, lab);
    program.Step();
    Equal(milestone.Points+3, orbit.Stock("research"));
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
    AssembleShuttle(program);
    AssembleShuttle(program);
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

void TimedAssembly()
{
    var program = LaunchReadyProgram();
    var type = rules.ShipType("heavy-freighter");
    program.Mars.Receive(new() { ["titanium"] = 12 });
    program.Assemble(program.Mars, 2, type.Id);
    Equal(1, program.Export().Space!.Ships.Length);
    Equal(type.BuildTicks, program.Mars.Export().Assembly!.Single().TicksLeft);
    Reject(() => program.Mars.Remove(2));
    program.Mars.Toggle(2);
    program.Step();
    program = SpaceProgram.Restore(rules, program.Export());
    Equal(type.BuildTicks, program.Mars.Export().Assembly!.Single().TicksLeft);
    program.Mars.Toggle(2);
    for (int i = 0; i < type.BuildTicks; i++) program.Step();
    Equal(type.Id, program.Export().Space!.Ships[1].Type);
    long fuel = program.Mars.Stock("propellant");
    program.Dispatch(program.Mars, 2, "mars-orbit-1", new() { ["alloy"] = 70 });
    Equal(fuel - (8 * type.FuelPercent + 99) / 100, program.Mars.Stock("propellant"));
}

void TelescopeDiscovery()
{
    var program = LaunchReadyProgram();
    const string orbitId = "mars-orbit-1";
    int center = 511 * Colony.Size + 511;
    int telescope = rules.Buildings.Single(b => b.SpaceRole == "telescope").Id;
    Reject(() => program.Dispatch(program.Mars,1,"proxima-world",null));
    Reject(() => program.Assemble(program.Mars,2,"deep-galaxy-ark"));
    Reject(() => program.Mars.Build(4,telescope));
    program.Dispatch(program.Mars,1,orbitId,null);
    for (int i = 0; i < 6; i++) program.Step();
    var orbit = program.Base(orbitId);
    orbit.Receive(new() { ["research"] = 7000, ["alloy"] = 500, ["silicon"] = 100, ["titanium"] = 65 });
    orbit.ActivateResearch("survey");
    orbit.ActivateResearch("orbital-optics");
    orbit.Build(center-1,1);
    orbit.Build(center+1,telescope);
    program.Step();
    Equal(600, program.InspectTelescope(orbit,center+1)!.Range);
    Reject(() => program.StartSurvey(orbit,center+1,true));
    long before = orbit.Stock("research");
    program.StartSurvey(orbit,center+1,false);
    Equal(before - 300,orbit.Stock("research"));
    Reject(() => program.StartSurvey(orbit,center+1,false));
    orbit.Toggle(center+1);
    program.Step();
    program = SpaceProgram.Restore(rules,program.Export());
    orbit = program.Base(orbitId);
    Equal(8 * 24,program.InspectTelescope(orbit,center+1)!.Survey!.TicksLeft);
    orbit.Toggle(center+1);
    for (int i = 0; i < 8 * 24 - 1; i++) program.Step();
    Equal(0,program.Export().Space!.Discovered!.Length);
    program.Step();
    Equal("proxima",program.Export().Space!.Discovered!.Single());
    Equal(false,program.InspectTelescope(orbit,center+1)!.Systems.Available);
    orbit.Upgrade(center+1);
    Equal(18_000,program.InspectTelescope(orbit,center+1)!.Range);
    Equal(true,program.InspectTelescope(orbit,center+1)!.Systems.Available);
    orbit.Upgrade(center+1);
    program.Step();
    Equal(540_000,program.InspectTelescope(orbit,center+1)!.Range);
    before = orbit.Stock("research");
    program.StartSurvey(orbit,center+1,true);
    Equal(before - 2000,orbit.Stock("research"));
    orbit.Receive(new() { ["alloy"] = 300, ["titanium"] = 120, ["silicon"] = 75 });
    orbit.Upgrade(center+1);
    program = SpaceProgram.Restore(rules,program.Export());
    orbit = program.Base(orbitId);
    var upgraded = program.InspectTelescope(orbit,center+1)!;
    Equal(4,upgraded.Level);
    Equal(540_000 * 30,upgraded.Range);
    Equal(30 * 24,upgraded.Survey!.TicksLeft);
    Reject(() => orbit.Upgrade(center+1));
    for (int i = 0; i < 30 * 24 - 1; i++) program.Step();
    Equal(1,program.Export().Space!.Discovered!.Length);
    program.Step();
    Equal(true,program.Export().Space!.Discovered!.Contains("lantern-primary"));
    Equal(true,program.InspectTelescope(orbit,center+1)!.Galaxies.Available);
    program.StartSurvey(orbit,center+1,true);
}

void IntergalacticTravel()
{
    Equal(10,StarChart.Galaxies.Count(g => StarChart.Distance(0,0,g.X,g.Y) > 150_000));
    var program = LaunchReadyProgram();
    var save = program.Export();
    save = save with { Space = save.Space! with
    {
        Discovered = ["cinder-primary","cinder-outer"],
        Ships = [new(1,"mars"), new(2,"mars",Type: "deep-galaxy-ark")]
    }};
    program = SpaceProgram.Restore(rules,save);
    program.Step();
    const string target = "cinder-primary-world";
    const string sample = "cinder-sample";
    Equal(false,program.MaterialAvailable(sample));
    Equal(false,program.VisitedGalaxies.Contains("cinder"));
    long fuel = program.Mars.Stock("propellant");
    Reject(() => program.Dispatch(program.Mars,1,target,null));
    Equal(fuel,program.Mars.Stock("propellant"));
    program.Mars.Receive(new() { ["propellant"] = 2000, ["silicon"] = 30 });
    Reject(() => program.Dispatch(program.Mars,2,target,new() { [sample] = 1 }));
    program.Dispatch(program.Mars,2,target,new() { ["alloy"] = 150, ["silicon"] = 30 });
    Equal(false,program.MaterialAvailable(sample));
    var flight = program.Export().Space!.Ships[1];
    Equal(true, flight.ArrivalTick - program.Mars.Tick > 600);
    Equal(true, program.Mars.Stock("propellant") < fuel + 2000);
    for (long tick = program.Mars.Tick; tick < flight.ArrivalTick; tick++) program.Step();
    var world = program.Base(target);
    Equal(true,program.MaterialAvailable(sample));
    Equal(false,program.MaterialAvailable("glacier-sample"));
    Equal(1,world.Export().Tiles!.Length);
    Equal(150L,world.Stock("alloy"));
    Equal(true,world.CanDock);
    Equal(42,world.Export().Climate!.BaseTemperature);
    var galaxy = StarChart.Galaxies.Single(g => g.Id == "cinder");
    world.Receive(new() { ["research"] = 1000 });
    world.ActivateResearch("survey");
    world.ActivateResearch(galaxy.Technology);
    int center = 511 * Colony.Size + 511;
    var lab = rules.Buildings.Single(b => b.Galaxy == galaxy.Id);
    world.Build(center-1,1);
    world.Build(center+1,lab.Id);
    program.Step();
    Equal(1L,world.Stock(galaxy.MaterialId));
    Equal(false,program.Mars.HasResearch("survey"));
    Reject(() => program.Mars.ActivateResearch(galaxy.Technology));
    var nextSave = program.Export();
    nextSave.Space!.Bases.Add("cinder-outer-orbit", world.Export() with
        { Tiles = [[center,rules.Buildings.Single(b => b.SpaceRole == "hub").Id]],
            Climate = null, PlanetBounds = null, LongOrbit = true, ActivatedResearch = [] });
    program = SpaceProgram.Restore(rules,nextSave);
    Equal(true,program.MaterialAvailable(sample));
    Equal(true,program.Base("cinder-outer-orbit").HasResearch(galaxy.Technology));
    Equal(false,program.Mars.HasResearch(galaxy.Technology));
}

void GeneratedUniverses()
{
    string Snapshot(UniverseCatalog catalog) =>
        System.Text.Json.JsonSerializer.Serialize(new { catalog.Galaxies,catalog.Systems,catalog.Sites });
    var first = UniverseCatalog.Restore(new(-4100));
    Equal(Snapshot(first),Snapshot(UniverseCatalog.Restore(new(-4100))));
    Equal(false,Snapshot(first) == Snapshot(UniverseCatalog.Restore(new(-4101))));
    Reject(() => UniverseCatalog.Restore(new(42,100)));
    var counts = new HashSet<int>();
    var localWidths = new HashSet<int>();
    var geology = new HashSet<string>();
    foreach (int seed in Enumerable.Range(-12,25).Append(int.MinValue).Append(int.MaxValue))
    {
        var program = SpaceProgram.StartCampaign(rules,new(seed,3));
        var settings = program.Export().Space!.Universe!;
        Equal(new UniverseSeed(seed),settings);
        Equal("sol",program.Mars.Site.System);
        Equal("iron",program.Mars.DepositAt(511 * Colony.Size + 512));
        var catalog = UniverseCatalog.Restore(settings);
        counts.Add(catalog.Galaxies.Length);
        localWidths.Add(catalog.Sites.Single(s => s.Id == "venus").Width);
        geology.Add(string.Join(",",catalog.Sites.Single(s => s.Id == "proxima-world").Deposits!));
        Equal(10,catalog.Galaxies.Count(g => StarChart.Distance(0,0,g.X,g.Y) > 150_000));
        Equal(true,catalog.Galaxies.All(g => g.Diameter is >= 8_000 and <= 12_000));
        Equal(true,catalog.Systems.GroupBy(s => s.Galaxy).All(group => group.Count() <= 20));
        Equal(true,catalog.Sites.GroupBy(s => s.System).All(group => group.Count() <= 10));
        Equal(true,catalog.Sites.All(site => site.Distance <= catalog.System(site.System).Diameter));
        Equal(true,catalog.Sites.Where(s => s.Profile is not null).All(s => s.Profile!.Valid));
    }
    Equal(2,counts.Count);
    Equal(true,localWidths.Count > 10);
    Equal(true,geology.Count > 10);
    var sameMap = SpaceProgram.StartCampaign(rules,new(0,3));
    var cleared = new Dictionary<int,string?>();
    var landing = sameMap.Mars.Site;
    for (int y = landing.Top; y < landing.Top + landing.Height; y++)
    for (int x = landing.Left; x < landing.Left + landing.Width; x++)
    {
        int index = y * Colony.Size + x;
        if (rules.FixedDeposit(index) is null) cleared[index] = null;
    }
    Reject(() => SpaceProgram.StartCampaign(rules,new(0,3,cleared)));
}

void GeneratedCampaignRoundTrip()
{
    var program = SpaceProgram.StartCampaign(rules,new(1400,8));
    var save = program.Export();
    var catalog = UniverseCatalog.Restore(save.Space!.Universe);
    var destination = catalog.Sites.Single(s => s.Id == "proxima-world");
    var station = catalog.Sites.Single(s => s.Id == "mars-orbit-1");
    int center = 511*Colony.Size+511;
    int Building(string role) => rules.Buildings.Single(b => b.SpaceRole == role).Id;
    save.Space!.Bases[station.Id] = save with
    {
        Tiles = [[center,Building("telescope")],[center+1,1]], Structures = [], PlanetBounds = null,
        LongOrbit = true, Map = new(station.Seed,station.Abundance), Space = null
    };
    save.Space.Bases[station.Id].Materials!["research"] = 300;
    save = save with { Space = save.Space with { Ships = [new(1,"mars",Type: "stellar-explorer")] } };
    program = SpaceProgram.Restore(rules,save);
    var mars = program.Mars;
    mars.Receive(new() { ["alloy"] = 1000, ["propellant"] = 300 });
    mars.Build(center - 1 - Colony.Size,Building("relay"));
    mars.Build(center - Colony.Size,Building("terminal"));
    program.Step();
    Reject(() => program.Dispatch(mars,1,destination.Id,null));
    program.StartSurvey(program.Base(station.Id),center,false);
    for (int i = 0; i < 192; i++) program.Step();
    Equal("proxima",program.Export().Space!.Discovered!.Single());
    program.Dispatch(mars,1,destination.Id,new() { ["alloy"] = 120 });
    int distance = StarChart.Distance(0,0,catalog.System("proxima").X,catalog.System("proxima").Y);
    var flight = program.Export().Space!.Ships.Single();
    Equal(2L + (distance*24L+959)/960,flight.ArrivalTick-mars.Tick);
    program = SpaceProgram.Restore(rules,program.Export());
    for (long tick = program.Mars.Tick; tick < flight.ArrivalTick; tick++) program.Step();
    var world = program.Base(destination.Id);
    Equal(destination.Width,world.Site.Width);
    Equal(destination.Height,world.Site.Height);
    Equal(destination.Seed,world.MapSettings.Seed);
    Equal(destination.Abundance,world.MapSettings.Abundance);
    Equal(120L,world.Stock("alloy"));
    var terrain = world.ClimateMap();
    var regionalDeposits = Enumerable.Range(0,world.Site.Width).Select(x => world.DepositAt(world.Site.Top*Colony.Size+world.Site.Left+x)).ToArray();
    var reloaded = SpaceProgram.Restore(rules,program.Export());
    var restored = reloaded.Base(destination.Id);
    Equal(true,terrain.SequenceEqual(restored.ClimateMap()));
    Equal(true,regionalDeposits.SequenceEqual(Enumerable.Range(0,restored.Site.Width)
        .Select(x => restored.DepositAt(restored.Site.Top*Colony.Size+restored.Site.Left+x))));
    Equal(new UniverseSeed(1400),reloaded.Export().Space!.Universe);
}

void PlanetaryEngineering()
{
    var initial = new Colony(rules, empty: true).Export();
    int center = 511 * Colony.Size + 511;
    var heat = rules.Buildings.Single(item => item.Effects.ContainsKey("temperature"));
    initial.Materials!["ore"] = 50_000;
    initial.Materials["propellant"] = 50_000;
    initial.Materials["food"] = 10_000;
    initial.Materials["water"] = 10_000;
    initial = initial with { Tiles = [[center, heat.Id], [center+1, 1], [center+2, 1], [center+3, 6]] };
    var mars = Colony.Restore(rules, initial);
    var europa = Colony.Restore(rules, initial, ColonySite.All.Single(site => site.Id == "europa"));
    var temperature = rules.World.Metrics.Single(metric => metric.Id == "temperature");
    int duration = rules.World.EngineeringSols * 24;
    for (int i = 0; i < 300; i++) { mars.Step(); europa.Step(); }
    Equal(temperature.Initial + (temperature.Target-temperature.Initial)*300/duration, mars.Export().Metrics!["temperature"]);
    Equal(temperature.Initial, europa.Export().Metrics!["temperature"]);
    mars = Colony.Restore(rules, mars.Export());
    for (int i = 300; i < duration-1; i++) mars.Step();
    Equal(temperature.Target-1, mars.Export().Metrics!["temperature"]);
    mars.Step();
    Equal(temperature.Target, mars.Export().Metrics!["temperature"]);
}

ColonySite Terminator() => rules.NewSite(ColonySite.All.Single(site => site.Id == "terminator"));
ClimateProfile TerminatorProfile() => rules.World.Climates!["terminator"];
IEnumerable<int> TerminatorTiles() =>
    from y in Enumerable.Range(Terminator().Top,Terminator().Height)
    from x in Enumerable.Range(Terminator().Left,Terminator().Width)
    select y*Colony.Size+x;

void ClimateMath()
{
    var site = Terminator();
    var profile = TerminatorProfile();
    var weather = new TileClimate(site,site.Seed,profile,profile.Initial);
    var same = new TileClimate(site,site.Seed,profile,profile.Initial);
    int tile = TerminatorTiles().First();
    Equal(weather.Elevation(tile), same.Elevation(tile));
    Equal(weather.Sunlight(tile), same.Sunlight(tile));
    Equal(weather.Temperature(tile), same.Temperature(tile));
    Equal(weather.Temperature(tile)+100, weather.Temperature(tile,new(10,0)));
    Equal(150, profile.AdjustOffset(200,20));
    Equal(-150, profile.AdjustOffset(-200,20));
    Equal(0, profile.AdjustOffset(0,20));
    Equal(true, profile.EffectiveBase(new(0,100)) > profile.EffectiveBase(new(0,20)));
    Equal(true, Math.Abs(profile.AdjustOffset(-200,100)) < 150);
}

void MeridianTerrain()
{
    var site = Terminator();
    Equal(30,site.Width);
    Equal(360,site.Height);
    var terrain = new TileClimate(site,site.Seed,TerminatorProfile(),new(200,0));
    int left = site.Top*Colony.Size+site.Left;
    int right = left+site.Width-1;
    for (int edge = 0; edge < 4; edge++)
    {
        Equal(true,terrain.Temperature(left+edge) < terrain.Temperature(left+edge+1));
        Equal(true,terrain.Temperature(right-edge) > terrain.Temperature(right-edge-1));
    }
    foreach (int index in TerminatorTiles())
    {
        if (site.Contains(index+1,new())) Equal(true,Math.Abs(terrain.Elevation(index+1)-terrain.Elevation(index)) <= 2);
        if (site.Contains(index+Colony.Size,new())) Equal(true,Math.Abs(terrain.Elevation(index+Colony.Size)-terrain.Elevation(index)) <= 2);
    }

    var colony = new Colony(rules,empty: true,site: site,climateSettings: new(200,0));
    var building = rules.Buildings.First(b => b.Footprint is { Width: 2, Height: 2 });
    int steep = TerminatorTiles().First(index => site.Contains(index,building.Footprint) &&
        Surface.Tiles(index,building.Footprint).All(terrain.Buildable) &&
        Surface.Tiles(index,building.Footprint).Max(terrain.Elevation) -
        Surface.Tiles(index,building.Footprint).Min(terrain.Elevation) > 2);
    Reject(() => colony.Build(steep,building.Id));
    var legacy = Colony.Restore(rules,colony.Export() with { PlanetBounds = new(108,110) },site);
    Equal(108,legacy.Site.Width);
}

void UnstaffedHousing()
{
    var habitat = rules.Buildings.First(b => b.Beds > 0);
    var solar = rules.Buildings.Single(b => b.Art == "solar");
    int anchor = 511*Colony.Size+511;
    var save = new Colony(rules,empty: true).Export();
    var colony = Colony.Restore(rules,save with
    {
        Population = 0,
        Tiles = [[anchor,solar.Id],[anchor+1,habitat.Id]]
    });

    colony.Step();
    using var powered = Status(colony);
    Equal(0,powered.RootElement.GetProperty("crewCapacity").GetInt32());
    Equal(habitat.Beds,powered.RootElement.GetProperty("housing").GetInt32());
    var info = System.Text.Json.JsonSerializer.SerializeToElement(colony.Inspect(anchor+1));
    Equal(true,info.GetProperty("running").GetBoolean());
    Equal(0,info.GetProperty("crew").GetInt32());

    colony.Toggle(anchor);
    colony.Step();
    using var offline = Status(colony);
    Equal(0,offline.RootElement.GetProperty("housing").GetInt32());
}

void PopulationGrowth()
{
    var colony = new Colony(rules);
    for (int hour = 0; hour < 5; hour++) colony.Step();
    Equal(4,colony.Export().Population);
    colony.Step();
    Equal(5,colony.Export().Population);

    var dome = rules.Buildings.Single(b => b.Name == "Settlement dome");
    var array = rules.Buildings.Single(b => b.Art == "solar");
    int start = 500*Colony.Size+500;
    var large = new Colony(rules,empty: true).Export();
    large.Materials!["food"] = large.Materials["water"] = 10_000;
    large = large with
    {
        Population = 99, MigrationCredit = 95, Tick = 5,
        Tiles = [[start,array.Id],[start+1,array.Id],[start+2,array.Id],
            [start+Colony.Size,dome.Id],[start+Colony.Size+3,dome.Id],[start+Colony.Size+6,dome.Id]]
    };
    colony = Colony.Restore(rules,large);
    colony.Step();
    Equal(100,colony.Export().Population);
    Equal(0,colony.Export().MigrationCredit);
    for (int hour = 0; hour < 6; hour++) colony.Step();
    Equal(101,colony.Export().Population);

    colony = Colony.Restore(rules,large with { Population = 199, MigrationCredit = 0 });
    colony.Step();
    Equal(200,colony.Export().Population);
    Equal(99,colony.Export().MigrationCredit);
    colony = Colony.Restore(rules,colony.Export());
    for (int hour = 0; hour < 6; hour++) colony.Step();
    Equal(202,colony.Export().Population);
    Equal(99,colony.Export().MigrationCredit);

    var solar = rules.Buildings.Single(b => b.Art == "solar");
    var habitat = rules.Buildings.First(b => b.Art == "habitat");
    var facility = rules.Buildings.Single(b => b.ColonistsPerHour > 0);
    int anchor = 511*Colony.Size+511;
    var site = Terminator();
    var outpost = new Colony(rules,empty: true,site: site).Export();
    var tiles = new[] { new[] {anchor,solar.Id}, [anchor+1,solar.Id], [anchor+2,habitat.Id], [anchor+Colony.Size,facility.Id] };
    colony = Colony.Restore(rules,outpost with { Population = 2,Tiles = tiles },site);
    colony.Step();
    Equal(3,colony.Export().Population);
    colony.Toggle(anchor+Colony.Size);
    colony.Step();
    Equal(3,colony.Export().Population);
}

void OceanTransitions()
{
    var site = Terminator();
    var profile = TerminatorProfile();
    var weather = new TileClimate(site,site.Seed,profile,new(-200,0));
    var shape = new Footprint(2,2);
    int ocean = TerminatorTiles().First(index => site.Contains(index,shape) &&
        Surface.Tiles(index,shape).All(tile => weather.Elevation(tile) is >= 1 and <= 6 &&
            weather.Water(weather.Elevation(tile),weather.Temperature(tile,new(-15,100))) == WaterState.Liquid));
    int shore = TerminatorTiles().First(index => weather.Elevation(index) == 6);
    int unusable = TerminatorTiles().First(index => weather.Elevation(index) is 0 or 15);
    var solar = rules.Buildings.Single(building => building.Art == "solar");
    var empty = new Colony(rules, empty: true, site: site, map: new(site.Seed,10,DensityPercent: true)).Export();
    var liquid = new ClimateSettings(-15,100);
    foreach (int degrees in new[] {-200,200})
    {
        var save = empty with { Climate = new(degrees,0), Tiles = [[ocean,solar.Id]],
            Structures = [new(ocean,2,2)] };
        var colony = Colony.Restore(rules, save, site);
        Equal(1, colony.ChangeClimate(liquid));
        Equal(0, colony.ChangeClimate(liquid));
        Equal(0, colony.Export().Tiles!.Length);
        colony.Build(shore, solar.Id);
        Reject(() => colony.Build(unusable, solar.Id));
        var restored = Colony.Restore(rules, colony.Export(), site);
        Equal(liquid, restored.Export().Climate);
        Equal((byte)solar.Id, restored.Grid[shore]);
    }
}

void ClimateOperation()
{
    var site = Terminator();
    var profile = TerminatorProfile();
    var weather = new TileClimate(site,site.Seed,profile,profile.Initial);
    var tiles = TerminatorTiles().Where(index => weather.Elevation(index) is >= 7 and <= 14).ToArray();
    int dark = tiles.First(index => weather.Sunlight(index)==0);
    int bright = tiles.First(index => weather.Sunlight(index)==100);
    int ice = tiles.First(index => weather.Temperature(index) is >= -500 and <= -250);
    var solar = rules.Buildings.Single(building => building.Art == "solar");
    var extractor = rules.Buildings.Single(building => building.Deposit == "ice");
    var empty = new Colony(rules, empty: true, site: site, map: new(site.Seed,10,
        new() { [ice] = "ice" },DensityPercent: true)).Export();
    var colony = Colony.Restore(rules,empty with { Tiles = [[dark,solar.Id],[bright,solar.Id],[ice,extractor.Id]] },site);
    colony.Step();
    using var initial = Status(colony);
    Equal((long)solar.Generation, initial.RootElement.GetProperty("supply").GetInt64());
    long water = colony.Stock("water");
    colony.ChangeClimate(new(0,60));
    colony.Step();
    Equal(true, colony.Stock("water") < water);
    colony.ChangeClimate(profile.Initial);
    water = colony.Stock("water");
    colony.Step();
    Equal(true, colony.Stock("water") > water);
}

void MapDensity()
{
    var session = new ColonySession(rules, new ConfigurationBuilder().Build());
    foreach (int abundance in new[] {2,3,15,16})
    {
        var result = (IStatusCodeHttpResult)session.Apply(new("new",Map: new(42,abundance)));
        Equal<int?>(abundance is >= 3 and <= 15 ? 200 : 400,result.StatusCode);
    }
    var legacy = new Colony(rules,map: new(42,175),empty: true);
    var restored = Colony.Restore(rules,legacy.Export());
    Equal(175,restored.MapSettings.Abundance);
    Equal(false,restored.MapSettings.DensityPercent);
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
