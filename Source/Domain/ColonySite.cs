namespace RedMeridian.Domain;

sealed record ColonySite(string Id, string Name, string Kind, int Width, int Seed, int Abundance, int Distance, int? Length = null)
{
    static readonly string[] AsteroidDeposits = ["iron", "titanium", "uranium"];
    static readonly string[] MoonDeposits = ["ice", "brine", "silicon"];
    public int Height => Length ?? Width;
    public int Left => (Colony.Size - Width) / 2;
    public int Top => (Colony.Size - Height) / 2;
    public bool PlanetaryEngineering => Kind is "mars" or "planet";
    public string[]? Deposits => Kind switch
    {
        "orbit" => [],
        "asteroid" => AsteroidDeposits,
        "moon" => MoonDeposits,
        _ => null
    };

    public static readonly ColonySite Mars = new("mars", "Mars Landing Sector", "mars", Colony.Size, 0, 100, 0);
    public static readonly ColonySite[] All =
    [
        Mars,
        new("venus", "Venus Survey Outpost", "planet", 112, 92431, 100, 2, 120),
        new("terminator", "The Terminator", "planet", 30, 57331, 10, 8, 360),
        new("mars-orbit-1", "Mars Orbit · Slot 1", "orbit", 5, 1, 0, 1, 150),
        new("mars-orbit-2", "Mars Orbit · Slot 2", "orbit", 5, 2, 0, 1, 150),
        new("ceres", "Ceres Mining Outpost", "asteroid", 32, 3107, 180, 3),
        new("europa", "Europa Ice Fields", "moon", 316, -17031, 130, 6),
        new("jupiter-orbit-1", "Jupiter Orbit · Slot 1", "orbit", 5, 3, 0, 5, 150),
        new("jupiter-orbit-2", "Jupiter Orbit · Slot 2", "orbit", 5, 4, 0, 5, 150)
    ];

    public bool Contains(int index, Footprint footprint)
    {
        int x = index % Colony.Size, y = index / Colony.Size;
        return Surface.Fits(index, footprint) && x >= Left && y >= Top &&
            x + footprint.Width <= Left + Width && y + footprint.Height <= Top + Height;
    }

    public bool Allows(Building building, bool restoring = false) =>
        (Kind == "mars" || !building.ForecastsWeather) &&
        (PlanetaryEngineering || restoring || building.Effects.Count == 0) &&
        (Kind == "orbit"
            ? building.Deposit is null && building.Effects.Count == 0 && building.Art != "wind" &&
                (building.Art != "launch" || building.SpaceRole == "assembly") &&
                building.SpaceRole != "terminal"
            : building.SpaceRole != "dock" && (building.Deposit is null || Deposits?.Contains(building.Deposit) != false));
}
