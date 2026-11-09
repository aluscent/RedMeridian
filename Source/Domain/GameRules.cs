using System.Text.Json;

namespace RedMeridian.Domain;

sealed record Material(string Id, string Name, string Symbol, long Initial);
sealed record Technology(string Id, string Name, long Points, string[] Requires);
sealed record Deposit(string Id, string Name, int[] Buckets, string[] Colors);
sealed record DepositOverride(int X, int Y, string Deposit);
sealed record StarterTile(int X, int Y, int Building);
sealed record Metric(string Id, string Name, string Unit, int Initial, int Target);
sealed record StormCycle(int Period, int Start, int End);
sealed record WorldRules(int StarterPopulation, int CrewPerColonist, int ColonistsPerRation,
    int ArrivalInterval, int ArrivalReserves, string[] LifeSupport, string ResearchMaterial,
    StormCycle Storm, int DepositBuckets, Deposit[] Deposits, DepositOverride[] DepositOverrides,
    StarterTile[] Starter, Metric[] Metrics);

sealed record Building
{
    public Footprint Footprint { get; init; } = new();
    public MineUpgrade[] Upgrades { get; init; } = [];
    public string? Image { get; init; }
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string Category { get; init; } = "";
    public string Art { get; init; } = "";
    public Dictionary<string,int> Cost { get; init; } = [];
    public Dictionary<string,int> Inputs { get; init; } = [];
    public Dictionary<string,int> Outputs { get; init; } = [];
    public Dictionary<string,int> Effects { get; init; } = [];
    public int Generation { get; init; }
    public int? StormGeneration { get; init; }
    public int Power { get; init; }
    public int Crew { get; init; }
    public int Beds { get; init; }
    public int Storage { get; init; }
    public int ChargeRate { get; init; }
    public int DischargeRate { get; init; }
    public int Priority { get; init; } = 70;
    public bool ForecastsWeather { get; init; }
    public string? Deposit { get; init; }
    public string? Research { get; init; }
}

sealed class GameRules
{
    readonly Dictionary<string,Technology> technologies;
    readonly Dictionary<int,string> depositOverrides;
    readonly string?[] depositBuckets;
    public Building[] Buildings { get; }
    public Material[] Materials { get; }
    public Technology[] Research { get; }
    public WorldRules World { get; }

    public GameRules(string folder)
    {
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
        };
        T Read<T>(string file) => JsonSerializer.Deserialize<T>(File.ReadAllText(Path.Combine(folder,file + ".json")),json)
            ?? throw new InvalidDataException($"Empty config: {file}.json");

        Materials = Read<Material[]>("materials");
        Research = Read<Technology[]>("research");
        World = Read<WorldRules>("world");
        Buildings = Read<Building[]>("buildings").OrderBy(b => b.Id).ToArray();

        var materialIds = Unique(Materials.Select(m => m.Id), "material");
        var metricIds = Unique(World.Metrics.Select(m => m.Id), "metric");
        var depositIds = Unique(World.Deposits.Select(d => d.Id), "deposit");
        Unique(Research.Select(t => t.Id), "research");
        technologies = Research.ToDictionary(t => t.Id);
        Require(Materials.All(m => m.Initial is >= 0 and <= Colony.StockLimit), "Invalid starting stock.");
        Require(World.Metrics.All(m => m.Target > m.Initial), "Metric target must exceed its starting value.");
        Require(World.DepositBuckets is > 0 and <= 4096 && World.StarterPopulation is > 0 and <= 1000 &&
            World.CrewPerColonist is > 0 and <= 10 && World.ColonistsPerRation is > 0 and <= 1000 &&
            World.ArrivalInterval > 0 && World.ArrivalReserves is >= 0 and <= 100 &&
            World.LifeSupport.Length > 0 && World.LifeSupport.Distinct().Count() == World.LifeSupport.Length &&
            World.LifeSupport.All(materialIds.Contains) && materialIds.Contains(World.ResearchMaterial),
            "Invalid world or life support settings.");
        Require(World.Storm.Period > 0 && World.Storm.Start >= 0 && World.Storm.Start < World.Storm.End &&
            World.Storm.End <= World.Storm.Period, "Invalid dust storm schedule.");

        depositBuckets = new string?[World.DepositBuckets];
        foreach (var deposit in World.Deposits)
        {
            Require(deposit.Colors.Length == 3, $"Deposit {deposit.Id} needs three colors.");
            foreach (int bucket in deposit.Buckets)
            {
                Require((uint)bucket < depositBuckets.Length && depositBuckets[bucket] is null,
                    $"Invalid or duplicate bucket {bucket} for {deposit.Id}.");
                depositBuckets[bucket] = deposit.Id;
            }
        }
        depositOverrides = new();
        foreach (var item in World.DepositOverrides)
            Require(InBounds(item.X,item.Y) && depositIds.Contains(item.Deposit) &&
                depositOverrides.TryAdd(item.Y * Colony.Size + item.X,item.Deposit), "Invalid or duplicate deposit override.");

        var visited = new HashSet<string>();
        var visiting = new HashSet<string>();
        void ValidateResearch(string id)
        {
            if (visited.Contains(id)) return;
            Require(technologies.TryGetValue(id, out var technology), $"Unknown research prerequisite {id}.");
            Require(visiting.Add(id), $"Research cycle at {id}.");
            Require(technology!.Points is >= 0 and <= Colony.StockLimit, $"Invalid research threshold at {id}.");
            foreach (var prerequisite in technology.Requires) ValidateResearch(prerequisite);
            visiting.Remove(id);
            visited.Add(id);
        }
        foreach (var item in Research) ValidateResearch(item.Id);

        Require(Buildings.Length is > 0 and <= byte.MaxValue &&
            Buildings.Select(b => b.Id).SequenceEqual(Enumerable.Range(1, Buildings.Length)),
            "Building IDs must be consecutive from 1 through the last building, at most 255.");
        foreach (var b in Buildings)
        {
            bool Amounts(Dictionary<string,int> amounts, HashSet<string> ids) =>
                amounts.All(entry => ids.Contains(entry.Key) && entry.Value is > 0 and <= 1000);
            Require(!string.IsNullOrWhiteSpace(b.Name) && !string.IsNullOrWhiteSpace(b.Category) &&
                Amounts(b.Cost,materialIds) && Amounts(b.Inputs,materialIds) && Amounts(b.Outputs,materialIds) &&
                Amounts(b.Effects,metricIds) && (b.Deposit is null || depositIds.Contains(b.Deposit)) &&
                (b.Research is null || technologies.ContainsKey(b.Research)) &&
                new[] { b.Power,b.Generation,b.StormGeneration ?? 0,b.Crew,b.Beds,b.Storage,b.ChargeRate,b.DischargeRate }
                    .All(value => value is >= 0 and <= 1000), $"Invalid building definition for ID {b.Id} ({b.Name}).");
            Require(Surface.Fits(0,b.Footprint) && b.Upgrades.Length <= 2 &&
                (b.Upgrades.Length == 0 || b.Deposit is not null && b.Outputs.Count > 0) &&
                b.Upgrades.All(u => u.Multiplier is >= 2 and <= 10 && Amounts(u.Cost,materialIds) &&
                    u.ExtraPower is >= 0 and <= 1000 && u.ExtraCrew is >= 0 and <= 1000),
                $"Invalid footprint or upgrades for {b.Name}.");
            Require(b.Image is null || System.Text.RegularExpressions.Regex.IsMatch(b.Image,
                @"^assets/(?:[a-zA-Z0-9_-]+/)*[a-zA-Z0-9_-]+[.]png$"), $"Invalid PNG path for {b.Name}.");
            Require((b.StormGeneration is null || b.Generation > 0) &&
                (b.Generation == 0 || (b.Power == 0 && b.Storage == 0 && b.Outputs.Count == 0 && b.Effects.Count == 0 && b.Beds == 0)) &&
                (b.Storage == 0 ? b.ChargeRate == 0 && b.DischargeRate == 0 :
                    b.ChargeRate > 0 && b.DischargeRate > 0 && b.Power == 0 && b.Crew == 0 && b.Beds == 0 &&
                    b.Inputs.Count == 0 && b.Outputs.Count == 0 && b.Effects.Count == 0),
                $"Incompatible power roles for {b.Name}.");
        }
        var starter = new HashSet<int>();
        foreach (var tile in World.Starter)
        {
            Require(InBounds(tile.X,tile.Y) && tile.Building > 0 && tile.Building <= Buildings.Length &&
                Surface.Fits(tile.Y * Colony.Size + tile.X,Buildings[tile.Building - 1].Footprint) &&
                Surface.Tiles(tile.Y * Colony.Size + tile.X,Buildings[tile.Building - 1].Footprint).All(starter.Add), "Invalid starter structure.");
            var b = Buildings[tile.Building - 1];
            Require(b.Deposit is null || b.Deposit == DepositAt(tile.Y * Colony.Size + tile.X),
                $"Starter {b.Name} is missing its deposit.");
        }

    }

    static bool InBounds(int x,int y) => (uint)x < Colony.Size && (uint)y < Colony.Size;
    static void Require(bool condition,string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
    static HashSet<string> Unique(IEnumerable<string> ids,string kind)
    {
        var result = new HashSet<string>();
        foreach (var id in ids) Require(!string.IsNullOrWhiteSpace(id) && result.Add(id), $"Invalid or duplicate {kind} ID '{id}'.");
        return result;
    }

    public string? FixedDeposit(int index) => depositOverrides.GetValueOrDefault(index);

    public string? DepositAt(int index,int seed = 0,int abundance = 100)
    {
        if (depositOverrides.TryGetValue(index,out string? deposit)) return deposit;
        uint hash = unchecked((uint)(index * 0x9e3779b9) ^ (uint)seed);
        hash = unchecked((hash ^ (hash >> 16)) * 0x7feb352du);
        hash ^= hash >> 15;
        string? natural = depositBuckets[hash % depositBuckets.Length];
        uint variation = (hash / (uint)depositBuckets.Length) % 100;
        if (natural is not null) return variation < abundance ? natural : null;
        if (abundance <= 100) return null;
        var deposits = World.Deposits;
        return deposits.Length > 0 && variation < abundance - 100
            ? deposits[(hash / (uint)depositBuckets.Length / 100) % deposits.Length].Id : null;
    }

    public bool Unlocked(string id,long points)
    {
        var tech = technologies[id];
        return points >= tech.Points && tech.Requires.All(prerequisite => Unlocked(prerequisite,points));
    }

    public Technology Technology(string id) => technologies[id];
}
