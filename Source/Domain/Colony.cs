using System.Diagnostics;

namespace RedMeridian.Domain;

record SaveData(int Version, int[][]? Tiles, long[]? Stock, int Population, long Tick, int[]? Planet,
    Dictionary<string,long>? Materials = null, Dictionary<string,int>? Metrics = null, long Charge = 0,
    MapSettings? Map = null, SavedStructure[]? Structures = null,
    Dictionary<int,WeatherForecast>? WeatherForecasts = null, SpaceSave? Space = null,
    string[]? ActivatedResearch = null);


sealed partial class Colony
{
    public const int Size = 1024;
    public const long StockLimit = 1_000_000_000_000;
    const int MaxPopulation = 100_000_000;
    readonly GameRules rules;
    public byte[] Grid { get; } = new byte[Size * Size];
    readonly int[] counts;
    readonly int[] allowedBuildings;
    readonly Surface surface;
    readonly Dictionary<int,int> levels = new();
    readonly HashSet<int> disabled = new();
    readonly HashSet<string> activatedResearch = new();
    readonly ProductionGroup[][] groups;
    readonly ProductionGroup[] producers, consumers, batteries;
    readonly ProductionGroup[] observatories;
    public MapSettings MapSettings => surface.Settings;
    public ColonySite Site { get; }
    public byte[] Widths => surface.Widths;
    public byte[] Heights => surface.Heights;
    readonly Dictionary<string,long> stock, delta;
    readonly Dictionary<string,int> planet;
    readonly PowerGrid power = new();
    readonly Queue<double> stepTimes = new();
    int population, housing, crewUsed;
    long tick, generated, used, demand;
    bool shortage;

    bool Storm => Site.Kind == "mars" && tick % rules.World.Storm.Period >= rules.World.Storm.Start &&
        tick % rules.World.Storm.Period < rules.World.Storm.End;

    public Colony(GameRules rules, bool stress = false, MapSettings? map = null, bool empty = false, ColonySite? site = null)
    {
        this.rules = rules;
        Site = site ?? ColonySite.Mars;
        allowedBuildings = rules.Buildings.Where(Site.Allows).Select(building => building.Id).ToArray();
        counts = new int[rules.Buildings.Length];
        surface = new Surface(rules,map);
        groups = rules.Buildings.Select(b => new[] { new ProductionGroup(b) }
            .Concat(b.Upgrades.Select(u => new ProductionGroup(b with
            {
                Power = b.Power + u.ExtraPower,
                Crew = b.Crew + u.ExtraCrew,
                Outputs = b.Outputs.ToDictionary(pair => pair.Key,pair => pair.Value * u.Multiplier)
            }))).ToArray()).ToArray();
        var allGroups = groups.SelectMany(group => group).ToArray();
        producers = allGroups.Where(g => g.Building.Generation > 0).OrderBy(g => g.Building.Inputs.Count > 0)
            .ThenBy(g => g.Building.Priority).ToArray();
        consumers = allGroups.Where(g => g.Building.Generation == 0 && g.Building.Storage == 0)
            .OrderBy(g => g.Building.Priority).ToArray();
        batteries = allGroups.Where(g => g.Building.Storage > 0).ToArray();
        observatories = consumers.Where(g => g.Building.ForecastsWeather).ToArray();
        stock = rules.Materials.ToDictionary(m => m.Id, m => m.Initial);
        delta = rules.Materials.ToDictionary(m => m.Id, _ => 0L);
        planet = rules.World.Metrics.ToDictionary(m => m.Id, m => m.Initial);
        population = rules.World.StarterPopulation;
        if (stress)
        {
            for (int i = 0; i < 100_000; i++)
                Place(unchecked(i * 499979) & (Grid.Length - 1), allowedBuildings[i % allowedBuildings.Length],new Footprint());
            foreach (var material in rules.Materials) stock[material.Id] = 100_000;
            population = 60_000;
        }
        else if (!empty)
        {
            foreach (var tile in rules.World.Starter) Place(tile.Y * Size + tile.X,tile.Building,rules.Buildings[tile.Building - 1].Footprint);
        }
        housing = rules.Buildings.Sum(b => counts[b.Id - 1] * b.Beds);
        RefreshCapacity();
        RefreshInitialOutput();
    }

    void RefreshInitialOutput()
    {
        generated = producers.Where(g => g.Building.Inputs.Count == 0 && g.Building.Crew == 0)
            .Sum(g => (long)g.Count * OutputPower(g.Building));
    }

    int OutputPower(Building b)
    {
        if (Storm) return b.StormGeneration ?? b.Generation;
        if (b.Art != "solar") return b.Generation;
        return Site.Kind switch
        {
            "orbit" => Site.Distance >= 5 ? b.Generation / 2 : b.Generation * 3 / 2,
            "moon" => b.Generation / 2,
            _ => b.Generation
        };
    }

    public void Step()
    {
        long start = Stopwatch.GetTimestamp();
        if (tick < long.MaxValue - 1) tick++;
        foreach (var material in rules.Materials) delta[material.Id] = -stock[material.Id];
        int crew = population * rules.World.CrewPerColonist;
        generated = 0;
        used = 0;
        housing = 0;
        foreach (var group in producers)
        {
            var producer = group.Building;
            int output = OutputPower(producer);
            int running = output == 0 ? 0 : Available(group, crew);
            if (producer.Inputs.Count > 0 && output > 0)
            {
                long missing = Math.Max(0,demand - generated - power.Available);
                running = (int)Math.Min(running,(missing + output - 1) / output);
            }
            Consume(group,running,ref crew);
            generated += (long)running * output;
        }

        foreach (var group in consumers)
        {
            var consumer = group.Building;
            int running = Available(group,crew);
            if (consumer.Power > 0)
                running = (int)Math.Min(running,(generated + power.Available - used) / consumer.Power);
            Consume(group,running,ref crew);
            used += (long)running * consumer.Power;
            housing += running * consumer.Beds;
            foreach (var (id,amount) in consumer.Outputs)
                stock[id] = Math.Min(StockLimit,stock[id] + (long)amount * running);
            if (running > 0)
            {
                foreach (var metric in rules.World.Metrics)
                    if (consumer.Effects.TryGetValue(metric.Id,out int increase))
                        planet[metric.Id] = (int)Math.Min(metric.Target, planet[metric.Id] + (long)increase * running);
            }
        }
        foreach (var battery in batteries) battery.Active = battery.Count;
        power.Settle(generated,used);
        crewUsed = population * rules.World.CrewPerColonist - crew;
        UpdatePopulation();
        RecordWeatherForecasts();
        foreach (var material in rules.Materials) delta[material.Id] += stock[material.Id];

        stepTimes.Enqueue(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        if (stepTimes.Count > 120) stepTimes.Dequeue();
    }

    int Available(ProductionGroup group,int crew)
    {
        var b = group.Building;
        int running = group.Count;
        if (b.Crew > 0) running = Math.Min(running,crew / b.Crew);
        foreach (var (id,amount) in b.Inputs) running = (int)Math.Min(running,stock[id] / amount);
        return running;
    }

    void Consume(ProductionGroup group,int running,ref int crew)
    {
        var b = group.Building;
        group.Active = running;
        crew -= running * b.Crew;
        foreach (var (id,amount) in b.Inputs) stock[id] -= (long)amount * running;
    }

    void UpdatePopulation()
    {
        var world = rules.World;
        int ration = (population + world.ColonistsPerRation - 1) / world.ColonistsPerRation;
        shortage = housing < population;
        bool surplus = true;
        foreach (var id in world.LifeSupport)
        {
            shortage |= stock[id] < ration;
            stock[id] = Math.Max(0,stock[id] - ration);
            surplus &= stock[id] >= (long)ration * world.ArrivalReserves;
        }
        if (tick % world.ArrivalInterval != 0) return;
        if (shortage && population > 1) population--;
        else if (Site.Kind == "mars" && !shortage && surplus && population < Math.Min(housing,MaxPopulation)) population++;
    }

    void RefreshCapacity()
    {
        power.Configure(batteries);
        demand = consumers.Sum(g => (long)g.Count * g.Building.Power);
    }

    public string ActivateResearch(string? id)
    {
        var milestone = rules.Research.FirstOrDefault(item => item.Id == id)
            ?? throw new InvalidOperationException("Select a research milestone.");
        if (activatedResearch.Contains(milestone.Id))
            throw new InvalidOperationException("This milestone is already active.");
        if (stock[rules.World.ResearchMaterial] < milestone.Points)
            throw new InvalidOperationException($"Requires {milestone.Points} research points.");
        if (!milestone.Requires.All(activatedResearch.Contains))
            throw new InvalidOperationException("Activate the prerequisite milestones first.");
        activatedResearch.Add(milestone.Id);
        return $"{milestone.Name} activated.";
    }

    public object Status(int revision,bool stress,bool paused,int speed,bool started = true,bool hasSave = false,
        int worldRevision = 0,int inspect = -1,bool developerMode = false) => new
    {
        revision, stress, paused, speed, tick, started, hasSave, worldRevision, developerMode, site = Site, allowedBuildings,
        inspected = Inspect(inspect), stock = new Dictionary<string,long>(stock),
        delta = new Dictionary<string,long>(delta), population, housing,
        supply = generated, demand, used, crewUsed, crewCapacity = population * rules.World.CrewPerColonist,
        batteryCharge = power.Charge, batteryCapacity = power.Capacity, batteryAvailable = power.Available,
        shortage, storm = Storm, planet = new Dictionary<string,int>(planet),
        complete = rules.World.Metrics.All(m => planet[m.Id] >= m.Target),
        unlocked = activatedResearch.ToArray(),
        counts = counts.ToArray(), active = groups.Select(group => group.Sum(g => g.Active)).ToArray(), total = counts.Sum(),
        tickMs = stepTimes.Count == 0 ? 0 : stepTimes.Order().ElementAt((int)Math.Ceiling(stepTimes.Count * .95) - 1)
    };
}
