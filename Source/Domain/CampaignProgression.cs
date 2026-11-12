namespace RedMeridian.Domain;

sealed partial class UniverseCatalog
{
    // Check the dependency chain using the actual game recipes and generated deposits.
    // This is a reachability check, independent of build order and the pace of play.
    public void ValidateProgression(GameRules rules, Colony landing)
    {
        var renewable = new HashSet<string>();
        var unlocked = new HashSet<string>();
        var buildable = new HashSet<int>();
        var starter = rules.World.Starter.Select(tile => tile.Building).ToHashSet();
        var deposits = new HashSet<string>();
        var site = landing.Site;
        for (int y = site.Top; y < site.Top+site.Height && deposits.Count < rules.World.Deposits.Length; y++)
        for (int x = site.Left; x < site.Left+site.Width; x++)
            if (landing.DepositAt(y*Colony.Size+x) is { } deposit) deposits.Add(deposit);

        bool Affordable(Dictionary<string,int> amounts) => amounts.All(item =>
            renewable.Contains(item.Key) || landing.Stock(item.Key) >= item.Value);
        bool Researched(string? id) => id is null || unlocked.Contains(id);
        bool changed;
        do
        {
            changed = false;
            foreach (var tech in rules.Research.Where(site.Allows))
                if (tech.Requires.All(unlocked.Contains) &&
                    (renewable.Contains(rules.World.ResearchMaterial) || landing.Stock(rules.World.ResearchMaterial) >= tech.Points))
                    changed |= unlocked.Add(tech.Id);
            foreach (var building in rules.Buildings.Where(b => site.Allows(b)))
            {
                if (!starter.Contains(building.Id) && !(Affordable(building.Cost) && Researched(building.Research) &&
                    (building.Deposit is null || deposits.Contains(building.Deposit)))) continue;
                changed |= buildable.Add(building.Id);
                if (building.Inputs.Keys.All(renewable.Contains))
                    foreach (string output in building.Outputs.Keys) changed |= renewable.Add(output);
            }
        } while (changed);

        Require(landing.Stock("alloy") >= rules.Buildings.Where(b => b.Art == "solar").Min(b => b.Cost.GetValueOrDefault("alloy")) &&
            rules.World.LifeSupport.All(renewable.Contains) && renewable.Contains("propellant") &&
            renewable.Contains(rules.World.ResearchMaterial) &&
            new[] {"relay","assembly","terminal"}.All(role => rules.Buildings.Any(b => b.SpaceRole == role && buildable.Contains(b.Id))),
            "The landing map must support power, life support, research and local spacecraft.");
        var telescope = rules.Buildings.Single(b => b.SpaceRole == "telescope");
        Require(Researched(telescope.Research) && Affordable(telescope.Cost) &&
            telescope.Upgrades.All(upgrade => Affordable(upgrade.Cost)), "The landing map is missing resources for telescope upgrades.");

        var shuttle = rules.ShipType(GameRules.LegacyShipType);
        var explorer = rules.ShipType("stellar-explorer");
        var clipper = rules.ShipType("galaxy-clipper");
        var ark = rules.ShipType("deep-galaxy-ark");
        foreach (var ship in new[] { shuttle,explorer,clipper,ark })
            Require(Researched(ship.Research) && Affordable(ship.Cost), $"The landing map is missing inputs for the {ship.Name}.");
        int initialOrbit = Sites.First(s => s.Id == "mars-orbit-1").Distance;
        Require(initialOrbit <= shuttle.Range && Sites.Where(s => s.System == StarChart.HomeSystem).All(s => s.Distance <= shuttle.Range),
            "The starting spacecraft must be able to reach the Solar System destinations.");

        foreach (var galaxy in Galaxies)
        {
            string primary = galaxy.Id == StarChart.HomeGalaxy ? StarChart.HomeSystem : galaxy.Id + "-primary";
            var origin = System(primary);
            var neighbors = Systems.Where(s => s.Galaxy == galaxy.Id && s.Id != primary).ToArray();
            int DistanceTo(StarSystem target) => StarChart.Distance(origin.X,origin.Y,target.X,target.Y);
            Require(neighbors.Length is >= 1 and < 20 &&
                neighbors.Any(s => DistanceTo(s) <= SpaceProgram.TelescopeRange(1)) &&
                neighbors.All(s => DistanceTo(s) <= Math.Min(explorer.Range,SpaceProgram.TelescopeRange(2))),
                $"The systems in {galaxy.Name} need a path through the first two telescope levels.");
            if (primary != StarChart.HomeSystem)
            {
                int distance = StarChart.Distance(0,0,origin.X,origin.Y);
                var ship = galaxy.Material is null ? clipper : ark;
                Require(distance <= Math.Min(ship.Range,SpaceProgram.TelescopeRange(3)),
                    $"{galaxy.Name} is beyond its discovery or travel tier.");
                RequireEntryResources(rules,Sites.Single(s => s.Id == primary + "-world"));
            }
        }
        RequireEntryResources(rules,Sites.Single(s => s.Id == "proxima-world"));
    }

    static void RequireEntryResources(GameRules rules, ColonySite site)
    {
        var profile = site.Profile!;
        var terrain = new TileClimate(site,site.Seed,profile,profile.Initial);
        var needed = new HashSet<string> { "iron","brine","silicon" };
        bool habitable = false;
        for (int y = site.Top; y < site.Top+site.Height && (needed.Count > 0 || !habitable); y++)
        for (int x = site.Left; x < site.Left+site.Width; x++)
        {
            int index = y*Colony.Size+x;
            if (!terrain.Buildable(index)) continue;
            if (rules.DepositAt(index,site.Seed,site.Abundance,true,site) is { } deposit) needed.Remove(deposit);
            habitable |= terrain.Temperature(index) is >= -200 and <= 400;
        }
        Require(needed.Count == 0 && habitable,$"{site.Name} needs buildable deposits and a viable settlement zone.");
    }

    static void Require(bool condition,string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
