using System.Text.Json.Serialization;

namespace RedMeridian.Domain;

sealed record MapSettings(int Seed = 0, int Abundance = 100, Dictionary<int,string?>? Paint = null,
    bool DensityPercent = false);
sealed record Footprint(int Width = 1,int Height = 1);
sealed record MineUpgrade(int Multiplier, Dictionary<string,int> Cost, int ExtraPower = 0, int ExtraCrew = 0);
sealed record SavedStructure(int Index,int Width = 1,int Height = 1,int Level = 1,bool Disabled = false);
sealed record TileEdit(int Index,int Type,int Width = 1,int Height = 1);

sealed class Surface
{
    readonly Dictionary<int,Structure> structures = new();
    readonly GameRules rules;
    readonly ColonySite? site;
    public MapSettings Settings { get; }
    public readonly record struct Structure(byte Type, byte Width, byte Height, int BuildingId)
    {
        public Footprint Footprint => new(Width,Height);
    }
    public Structure StructureAt(int anchor) => structures.GetValueOrDefault(anchor);
    public IEnumerable<KeyValuePair<int,Structure>> Structures => structures;

    public Surface(GameRules rules,MapSettings? settings,ColonySite? site = null)
    {
        this.rules = rules;
        this.site = site;
        settings ??= new();
        if ((settings.DensityPercent ? settings.Abundance is < 3 or > 15 : settings.Abundance is < 0 or > 200) ||
            settings.Paint?.Count > 16_384 ||
            settings.Paint?.Any(pair => (uint)pair.Key >= Colony.Size * Colony.Size ||
                pair.Value is not null && !rules.World.Deposits.Any(d => d.Id == pair.Value) ||
                site?.ProtectLandingDeposits != false && rules.FixedDeposit(pair.Key) is not null) == true)
            throw new InvalidOperationException("Invalid map settings or edit to a protected landing deposit.");
        Settings = settings with { Paint = settings.Paint is null ? new() : new(settings.Paint) };
    }

    public string? DepositAt(int index)
    {
        if (Settings.Paint!.TryGetValue(index,out var deposit)) return deposit;
        return rules.DepositAt(index,Settings.Seed,Settings.Abundance,Settings.DensityPercent,site);
    }

    public int AnchorAt(int index)
    {
        if ((uint)index >= Colony.Size * Colony.Size) return -1;
        if (structures.ContainsKey(index)) return index;
        // Footprints extend at most three cells right and down from their anchor.
        int x = index % Colony.Size, y = index / Colony.Size;
        for (int dy = 0; dy <= Math.Min(y,MaxFootprint - 1); dy++)
        for (int dx = 0; dx <= Math.Min(x,MaxFootprint - 1); dx++)
        {
            int anchor = index - dy * Colony.Size - dx;
            if (structures.TryGetValue(anchor,out var structure) && dx < structure.Width && dy < structure.Height)
                return anchor;
        }
        return -1;
    }

    const int MaxFootprint = 4;
    public static bool Fits(int index,Footprint shape) => (uint)index < Colony.Size * Colony.Size &&
        shape.Width is >= 1 and <= MaxFootprint && shape.Height is >= 1 and <= MaxFootprint &&
        index % Colony.Size + shape.Width <= Colony.Size && index / Colony.Size + shape.Height <= Colony.Size;

    public static IEnumerable<int> Tiles(int index,Footprint shape)
    {
        for (int y = 0; y < shape.Height; y++)
        for (int x = 0; x < shape.Width; x++)
            yield return index + x + y * Colony.Size;
    }

    public bool Open(int index,Footprint shape) => Fits(index,shape) &&
        Tiles(index,shape).All(tile => AnchorAt(tile) < 0);

    public void Occupy(int index,int type,Footprint shape,int buildingId) =>
        structures.Add(index,new((byte)type,(byte)shape.Width,(byte)shape.Height,buildingId));

    public void Clear(int index) => structures.Remove(index);
}

sealed class ProductionGroup(Building building, int sunlight = 100)
{
    SortedSet<int>? members;
    int[]? order;
    public Building Building { get; } = building;
    public int Sunlight { get; } = sunlight;
    public int Count => members?.Count ?? 0;
    public int Active { get; set; }
    public IEnumerable<int> ActiveMembers => members?.Take(Active) ?? [];

    public void Add(int index)
    {
        (members ??= new()).Add(index);
        order = null;
        Active = 0;
    }

    public void Remove(int index)
    {
        if (members is null) return;
        members.Remove(index);
        if (members.Count == 0) members = null;
        order = null;
        Active = 0;
    }

    public bool IsRunning(int index)
    {
        if (Active == 0 || members is null) return false;
        order ??= members.ToArray();
        int rank = Array.BinarySearch(order,index);
        return rank >= 0 && rank < Active;
    }
}
