namespace RedMeridian.Domain;

sealed record ColonySite(string Id, string Name, string Kind, int Size, int Seed, int Abundance, int Distance)
{
    static readonly string[] AsteroidDeposits = ["iron", "titanium", "uranium"];
    static readonly string[] MoonDeposits = ["ice", "brine", "silicon"];
    public int Edge => (Colony.Size - Size) / 2;
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
        new("mars-orbit-1", "Mars Orbit · Slot 1", "orbit", 16, 1, 0, 1),
        new("mars-orbit-2", "Mars Orbit · Slot 2", "orbit", 16, 2, 0, 1),
        new("ceres", "Ceres Mining Outpost", "asteroid", 32, 3107, 180, 3),
        new("europa", "Europa Ice Fields", "moon", 316, -17031, 130, 6),
        new("jupiter-orbit-1", "Jupiter Orbit · Slot 1", "orbit", 16, 3, 0, 5),
        new("jupiter-orbit-2", "Jupiter Orbit · Slot 2", "orbit", 16, 4, 0, 5)
    ];

    public bool Contains(int index, Footprint footprint)
    {
        int x = index % Colony.Size, y = index / Colony.Size;
        return Surface.Fits(index, footprint) && x >= Edge && y >= Edge &&
            x + footprint.Width <= Edge + Size && y + footprint.Height <= Edge + Size;
    }

    public bool Allows(Building building) =>
        (Kind == "mars" || !building.ForecastsWeather) &&
        (Kind == "orbit"
            ? building.Deposit is null && building.Effects.Count == 0 && building.Art != "wind" &&
                (building.Art != "launch" || building.SpaceRole == "assembly") &&
                building.SpaceRole != "terminal"
            : building.SpaceRole != "dock" && (building.Deposit is null || Deposits?.Contains(building.Deposit) != false));
}
