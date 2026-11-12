namespace RedMeridian.Domain;

sealed record Galaxy(string Id, string Name, int X, int Y, int Diameter,
    string? Material = null, string? Specialty = null, string? Study = null,
    string? World = null, int Temperature = 0)
{
    public string Technology => Id + "-survey";
    public string MaterialId => Id + "-sample";
}

sealed record StarSystem(string Id, string Name, string Galaxy, int X, int Y, int Diameter);

static class StarChart
{
    public const string HomeGalaxy = "milky-way";
    public const string HomeSystem = "sol";
    public const int TicksPerSol = 24;

    public static readonly Galaxy[] Galaxies =
    [
        new(HomeGalaxy, "Milky Way", 0, 0, 10_000),
        new("lantern", "Lantern Galaxy", 26_000, 12_000, 8_400),
        new("twin-veil", "Twin Veil Galaxy", -45_000, 27_000, 11_000),
        new("cinder", "Cinder Galaxy", 157_000, 44_000, 9_400, "Ember glass", "EG", "Volcanic glass optics", "Ashfall", 42),
        new("glacier", "Glacier Galaxy", 173_000, -52_000, 8_200, "Cryosalt", "CS", "Cryogenic brine chemistry", "Rime", -56),
        new("prism", "Prism Galaxy", 98_000, 166_000, 11_200, "Prism quartz", "PQ", "Spectral lattice analysis", "Facet", 12),
        new("hollow", "Hollow Galaxy", -183_000, 71_000, 10_100, "Void ceramic", "VC", "Porous ceramic synthesis", "Basin", -18),
        new("saffron", "Saffron Galaxy", -76_000, -199_000, 8_800, "Saffron clay", "SC", "Mineral catalyst design", "Ochre", 36),
        new("cobalt", "Cobalt Galaxy", 235_000, 13_000, 10_600, "Cobalt nodules", "CN", "Deep-crust metallurgy", "Ultramarine", -6),
        new("marrow", "Marrow Galaxy", -209_000, -135_000, 11_700, "Marrow chalk", "MC", "Fossil carbonate analysis", "Pale Reach", 2),
        new("lumen", "Lumen Galaxy", 105_000, -242_000, 9_100, "Lumen phosphor", "LP", "Persistent phosphorescence", "Afterlight", 24),
        new("indigo", "Indigo Galaxy", -280_000, 48_000, 12_000, "Indigo sulfide", "IS", "Sulfur semiconductor growth", "Dusk", -32),
        new("crown", "Crown Galaxy", 260_000, 180_000, 10_800, "Crown iridium", "CI", "Iridium matrix fabrication", "Diadem", 15)
    ];

    public static readonly StarSystem[] Systems =
    [
        new(HomeSystem, "Solar System", HomeGalaxy, 0, 0, 3),
        new("proxima", "Proxima", HomeGalaxy, 410, 160, 2),
        new("kepler", "Kepler", HomeGalaxy, -2_600, 1_300, 1),
        .. Galaxies.Skip(1).SelectMany(galaxy => new[]
        {
            new StarSystem(galaxy.Id + "-primary", galaxy.Name.Replace(" Galaxy", "") + " Prime", galaxy.Id, galaxy.X, galaxy.Y, 2),
            new StarSystem(galaxy.Id + "-outer", galaxy.Name.Replace(" Galaxy", "") + " Reach", galaxy.Id,
                galaxy.X + galaxy.Diameter / 4, galaxy.Y - 600, 1)
        })
    ];

    public static IEnumerable<ColonySite> RemoteSites => Systems.Where(system => system.Id != HomeSystem)
        .SelectMany((system, index) =>
        {
            var galaxy = Galaxies.Single(g => g.Id == system.Galaxy);
            string name = system.Id.EndsWith("-primary") && galaxy.World is not null ? galaxy.World : system.Name + " Survey World";
            return new ColonySite[]
            {
                new(system.Id + "-world", name, "planet", 100, 4163 + index * 1987, 6 + index % 10, 1, 100, system.Galaxy, system.Id),
                new(system.Id + "-orbit", system.Name + " Orbit", "orbit", 5, 19 + index, 0, 1, 150, system.Galaxy, system.Id)
            };
        });

    public static int Distance(int x, int y, int otherX, int otherY)
    {
        double dx = x - otherX, dy = y - otherY;
        return (int)Math.Ceiling(Math.Sqrt(dx * dx + dy * dy));
    }
}
