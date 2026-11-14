namespace RedMeridian.Domain;

sealed record ColonySite(string Id, string Name, string Kind, int Width, int Seed, int Abundance, int Distance, int? Length = null,
    string Galaxy = StarChart.HomeGalaxy, string System = StarChart.HomeSystem,
    bool Generated = false, string[]? RegionDeposits = null, ClimateProfile? Profile = null,
    string? RestrictedResearch = null)
{
    static readonly string[] AsteroidDeposits = ["iron", "titanium", "uranium"];
    static readonly string[] MoonDeposits = ["ice", "brine", "silicon"];
    public int Height => Length ?? Width;
    public string RegionId => Id;
    public string MapId => MapPrefix + RegionId;
    public const string MapPrefix = "map:";
    public int Left => (Colony.Size - Width) / 2;
    public int Top => (Colony.Size - Height) / 2;
    public bool PlanetaryEngineering => Kind is "mars" or "planet";
    public string[]? Deposits => RegionDeposits ?? (Kind switch
    {
        "orbit" => [],
        "asteroid" => AsteroidDeposits,
        "moon" => MoonDeposits,
        _ => null
    });
    public bool ProtectLandingDeposits => !Generated || Kind == "mars";

    public static readonly ColonySite Mars = new("mars", "Mars Landing Sector", "mars", Colony.Size, 0, 100, 0);
    public static readonly ColonySite[] All =
    [
        Mars,
        new("venus", "Venus Survey Outpost", "planet", 112, 92431, 100, 2, 120),
        new("terminator", "The Terminator", "planet", 30, 57331, 10, 3, 360),
        new("mars-orbit-1", "Mars Orbit · Slot 1", "orbit", 5, 1, 0, 1, 150),
        new("mars-orbit-2", "Mars Orbit · Slot 2", "orbit", 5, 2, 0, 1, 150),
        new("ceres", "Ceres Mining Outpost", "asteroid", 32, 3107, 180, 3),
        new("europa", "Europa Ice Fields", "moon", 316, -17031, 130, 3),
        new("jupiter-orbit-1", "Jupiter Orbit · Slot 1", "orbit", 5, 3, 0, 3, 150),
        new("jupiter-orbit-2", "Jupiter Orbit · Slot 2", "orbit", 5, 4, 0, 3, 150),
        .. StarChart.RemoteSites
    ];

    public bool Contains(int index, Footprint footprint)
    {
        int x = index % Colony.Size, y = index / Colony.Size;
        return Surface.Fits(index, footprint) && x >= Left && y >= Top &&
            x + footprint.Width <= Left + Width && y + footprint.Height <= Top + Height;
    }

    public bool Allows(Building building, bool restoring = false) =>
        (building.Galaxy is null || building.Galaxy == Galaxy) &&
        (RestrictedResearch is null || building.Research != RestrictedResearch) &&
        (building.SpaceRole != "telescope" || Kind == "orbit") &&
        (Kind == "mars" || !building.ForecastsWeather) &&
        (PlanetaryEngineering || restoring || building.Effects.Count == 0) &&
        (Kind == "orbit"
            ? building.Deposit is null && building.Effects.Count == 0 && building.Art != "wind" &&
                (building.Art != "launch" || building.SpaceRole == "assembly") &&
                (restoring || building.Id != 25) && // Core drilling is a surface facility.
                building.SpaceRole != "terminal"
            : building.SpaceRole != "dock" && (building.Deposit is null || Deposits?.Contains(building.Deposit) != false));

    public bool Allows(Technology technology) =>
        (technology.Galaxy is null || technology.Galaxy == Galaxy) && technology.Id != RestrictedResearch;
}
