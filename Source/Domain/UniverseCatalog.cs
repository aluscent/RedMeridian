namespace RedMeridian.Domain;

sealed record UniverseSeed(int Seed, int GeneratorVersion = 1);

sealed partial class UniverseCatalog(UniverseSeed? settings, Galaxy[] galaxies, StarSystem[] systems, ColonySite[] sites)
{
    public static UniverseCatalog Legacy { get; } = new(null,StarChart.Galaxies,StarChart.Systems,ColonySite.All);
    public UniverseSeed? Settings { get; } = settings;
    public Galaxy[] Galaxies { get; } = galaxies;
    public StarSystem[] Systems { get; } = systems;
    public ColonySite[] Sites { get; } = sites;
    public StarSystem System(string id) => Systems.Single(system => system.Id == id);

    public static UniverseCatalog Restore(UniverseSeed? settings) => settings?.GeneratorVersion switch
    {
        null => Legacy,
        1 => GenerateV1(settings),
        _ => throw new InvalidOperationException("This save uses an unsupported universe generator version.")
    };

    // Keep the rules and random sequence of each shipped generator version stable.
    static UniverseCatalog GenerateV1(UniverseSeed settings)
    {
        var random = new SeededSequence(settings.Seed);
        var galaxies = new List<Galaxy> { StarChart.Galaxies[0] with { Diameter = random.Next(8_000,12_000) } };
        var nearby = StarChart.Galaxies.Skip(1).Take(2).OrderBy(_ => random.Next(0,int.MaxValue))
            .Take(random.Next(1,2));
        foreach (var template in nearby.Concat(StarChart.Galaxies.Skip(3)))
        {
            bool distant = template.Material is not null;
            var point = Point(random,distant ? 155_000 : 21_000,distant ? 310_000 : 58_000,
                (x,y) => galaxies.All(g => StarChart.Distance(x,y,g.X,g.Y) >= 12_000));
            galaxies.Add(template with
            {
                X = point.X, Y = point.Y, Diameter = random.Next(8_000,12_000),
                Temperature = Math.Clamp(template.Temperature + random.Next(-12,12),-55,43)
            });
        }

        var systems = new List<StarSystem> { new(StarChart.HomeSystem,"Solar System",StarChart.HomeGalaxy,0,0,3) };
        foreach (var galaxy in galaxies)
        {
            bool home = galaxy.Id == StarChart.HomeGalaxy;
            string name = galaxy.Name.Replace(" Galaxy","");
            if (!home) systems.Add(new(galaxy.Id + "-primary",name + " Prime",galaxy.Id,galaxy.X,galaxy.Y,random.Next(1,3)));
            int count = random.Next(home ? 2 : 1,5);
            for (int i = 0; i < count; i++)
            {
                var point = Point(random,i == 0 ? 260 : 700,i == 0 ? 570 : galaxy.Diameter / 2,
                    (x,y) => systems.Where(s => s.Galaxy == galaxy.Id)
                        .All(s => StarChart.Distance(x+galaxy.X,y+galaxy.Y,s.X,s.Y) >= 160));
                var (id,label) = (home,i) switch
                {
                    (true,0) => ("proxima","Proxima"),
                    (true,1) => ("kepler","Kepler"),
                    (true,_) => ($"sol-neighbor-{i+1}",$"Helios {i+1}"),
                    (false,0) => (galaxy.Id + "-outer",name + " Reach"),
                    _ => ($"{galaxy.Id}-system-{i+1}",$"{name} {i+1}")
                };
                systems.Add(new(id,label,galaxy.Id,point.X+galaxy.X,point.Y+galaxy.Y,random.Next(1,3)));
            }
        }

        string[] deposits = ["iron","ice","silicon","titanium","brine","uranium","vent"];
        string[] asteroidDeposits = ["iron","titanium","uranium"], moonDeposits = ["ice","brine","silicon"];
        string[] regionalTracks = ["thermal","algae","hydrology"];
        var unavailableResearch = galaxies.Skip(1).ToDictionary(g => g.Id,g => regionalTracks[random.Next(0,2)]);
        var sites = new List<ColonySite>();
        foreach (var site in ColonySite.All.Where(s => s.System == StarChart.HomeSystem))
        {
            var generated = site with { Generated = true, Seed = random.Next(int.MinValue,int.MaxValue),
                Abundance = site.Kind == "orbit" ? 0 : random.Next(6,14) };
            generated = site.Id switch
            {
                "mars" => generated with { Seed = settings.Seed },
                "venus" => generated with { Width = random.Next(94,116), Length = random.Next(94,116), Distance = random.Next(1,2),
                    Profile = Climate(random,31) },
                "terminator" => generated with { Width = random.Next(27,30), Length = random.Next(330,400),
                    Profile = new(0,0,random.Next(92,106),125,50,35,0,45) },
                "ceres" => generated with { Width = random.Next(35,54), Distance = random.Next(2,3) },
                "europa" => generated with { Width = random.Next(68,100) },
                _ => generated
            };
            sites.Add(generated);
        }
        foreach (var system in systems.Where(s => s.Id != StarChart.HomeSystem))
        {
            var galaxy = galaxies.Single(g => g.Id == system.Galaxy);
            string? restricted = unavailableResearch.GetValueOrDefault(galaxy.Id);
            string worldName = system.Id.EndsWith("-primary") && galaxy.World is not null ? galaxy.World : system.Name + " Survey World";
            bool entryWorld = system.Id.EndsWith("-primary") || system.Id == "proxima";
            string[] region = PickDeposits(deposits);
            if (entryWorld) region = region.Union(["iron","brine","silicon"]).ToArray();
            sites.Add(MakeSite(system,"world",worldName,"planet",random.Next(94,116),random.Next(94,116),
                region,Climate(random,galaxy.Temperature,entryWorld),restricted));
            sites.Add(MakeSite(system,"orbit",system.Name + " Orbit","orbit",5,150,[],null,restricted));
            if (random.Next(0,1) == 1)
                sites.Add(MakeSite(system,"belt",system.Name + " Belt","asteroid",random.Next(32,52),null,
                    PickDeposits(asteroidDeposits),null,restricted));
            if (random.Next(0,1) == 1)
                sites.Add(MakeSite(system,"moon",system.Name + " Ice Moon","moon",random.Next(58,98),null,
                    PickDeposits(moonDeposits),null,restricted));
        }
        return new(settings,galaxies.ToArray(),systems.ToArray(),sites.ToArray());

        ColonySite MakeSite(StarSystem system,string suffix,string name,string kind,int width,int? height,
            string[] region,ClimateProfile? profile,string? restricted) =>
            new(system.Id + "-" + suffix,name,kind,width,random.Next(int.MinValue,int.MaxValue),
                kind == "orbit" ? 0 : random.Next(5,15),random.Next(1,system.Diameter),height,system.Galaxy,system.Id,
                true,region,profile,restricted);

        string[] PickDeposits(string[] choices) =>
            choices.OrderBy(_ => random.Next(0,int.MaxValue)).Take(random.Next(2,choices.Length)).ToArray();
    }

    static ClimateProfile Climate(SeededSequence random,int baseline,bool entryWorld = false) =>
        new(Math.Clamp(baseline+random.Next(-9,9),entryWorld ? -32 : -48,entryWorld ? 34 : 39),
            random.Next(5,48),random.Next(entryWorld ? 25 : 15,40),
            random.Next(15,45),50,20,-5,50);

    static (int X,int Y) Point(SeededSequence random,int minimum,int maximum,Func<int,int,bool> open)
    {
        for (int attempt = 0; attempt < 512; attempt++)
        {
            int x = random.Next(-maximum,maximum), y = random.Next(-maximum,maximum);
            int distance = StarChart.Distance(0,0,x,y);
            if (distance >= minimum && distance <= maximum && open(x,y)) return (x,y);
        }
        throw new InvalidOperationException("Could not place the campaign's star chart. Try another seed.");
    }

    sealed class SeededSequence(int seed)
    {
        uint state = unchecked((uint)seed);
        public int Next(int minimum,int maximum)
        {
            state = unchecked(state + 0x9e3779b9u);
            uint value = state;
            value = unchecked((value ^ (value >> 16)) * 0x21f0aaadu);
            value = unchecked((value ^ (value >> 15)) * 0x735a2d97u);
            value ^= value >> 15;
            return (int)(minimum + value % ((long)maximum-minimum+1));
        }
    }
}
