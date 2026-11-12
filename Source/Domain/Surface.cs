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
    readonly int[] owner = new int[Colony.Size * Colony.Size];
    readonly GameRules rules;
    readonly ColonySite? site;
    public MapSettings Settings { get; }
    public byte[] Widths { get; } = new byte[Colony.Size * Colony.Size];
    public byte[] Heights { get; } = new byte[Colony.Size * Colony.Size];

    public Surface(GameRules rules,MapSettings? settings,ColonySite? site = null)
    {
        this.rules = rules;
        this.site = site;
        settings ??= new();
        if ((settings.DensityPercent ? settings.Abundance is < 3 or > 15 : settings.Abundance is < 0 or > 200) ||
            settings.Paint?.Count > 16_384 ||
            settings.Paint?.Any(pair => (uint)pair.Key >= owner.Length ||
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

    public int AnchorAt(int index) => (uint)index < owner.Length ? owner[index] - 1 : -1;

    public static bool Fits(int index,Footprint shape) => (uint)index < Colony.Size * Colony.Size &&
        shape.Width is >= 1 and <= 4 && shape.Height is >= 1 and <= 4 &&
        index % Colony.Size + shape.Width <= Colony.Size && index / Colony.Size + shape.Height <= Colony.Size;

    public static IEnumerable<int> Tiles(int index,Footprint shape)
    {
        for (int y = 0; y < shape.Height; y++)
        for (int x = 0; x < shape.Width; x++)
            yield return index + x + y * Colony.Size;
    }

    public bool Open(int index,Footprint shape) => Fits(index,shape) &&
        Tiles(index,shape).All(tile => owner[tile] == 0);

    public void Occupy(int index,Footprint shape)
    {
        foreach (int tile in Tiles(index,shape)) owner[tile] = index + 1;
        Widths[index] = (byte)shape.Width;
        Heights[index] = (byte)shape.Height;
    }

    public void Clear(int index)
    {
        foreach (int tile in Tiles(index,new(Widths[index],Heights[index]))) owner[tile] = 0;
        Widths[index] = Heights[index] = 0;
    }
}

sealed class ProductionGroup(Building building, int sunlight = 100)
{
    readonly SortedSet<int> members = new();
    int[]? order;
    public Building Building { get; } = building;
    public int Sunlight { get; } = sunlight;
    public int Count => members.Count;
    public int Active { get; set; }
    public IEnumerable<int> ActiveMembers => members.Take(Active);

    public void Add(int index)
    {
        members.Add(index);
        order = null;
        Active = 0;
    }

    public void Remove(int index)
    {
        members.Remove(index);
        order = null;
        Active = 0;
    }

    public bool IsRunning(int index)
    {
        if (Active == 0) return false;
        order ??= members.ToArray();
        int rank = Array.BinarySearch(order,index);
        return rank >= 0 && rank < Active;
    }
}
