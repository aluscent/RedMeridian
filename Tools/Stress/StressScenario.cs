using RedMeridian.Domain;

sealed class StressScenario
{
    readonly GameRules rules;
    readonly UniverseCatalog universe;
    public SaveData InitialSave { get; }

    public StressScenario(GameRules rules, int regions, int orbits, int shipCount, int routeCount)
    {
        this.rules = rules;
        var sites = new ColonySite[regions + orbits];
        sites[0] = rules.NewSite(ColonySite.Mars);
        for (int i = 1; i < regions; i++)
            sites[i] = new($"stress-region-{i}",$"Region {i}","planet",100,i,8,i % 12, Generated: true);
        for (int i = 0; i < orbits; i++)
            sites[regions + i] = new($"stress-orbit-{i}",$"Orbit {i}","orbit",5,regions + i,0,i % 12,150);
        universe = new(null,StarChart.Galaxies,StarChart.Systems,sites);

        var bases = new Dictionary<string,SaveData>(sites.Length - 1);
        for (int i = 1; i < sites.Length; i++) bases.Add(sites[i].Id,BaseSave(sites[i],i));

        var shipType = rules.ShipType(GameRules.LegacyShipType);
        var ships = new Spacecraft[shipCount];
        var routes = new CargoRoute[routeCount];
        for (int i = 0; i < shipCount; i++)
        {
            string source = sites[i % sites.Length].Id;
            string target = sites[(i % sites.Length + 1) % sites.Length].Id;
            string material = i % 2 == 0 ? "alloy" : "water";
            // A synchronized first wave exercises the arrival path before recurring departures.
            ships[i] = i < routeCount && i % 2 == 0
                ? new(i + 1,source,target,1,new() { [material] = Math.Min(shipType.Payload,10) }, Type: shipType.Id)
                : new(i + 1,source,Type: shipType.Id);
            if (i < routeCount) routes[i] = new(i + 1,source,target,material,10,1_000);
        }
        InitialSave = BaseSave(sites[0],0) with { Space = new(bases,ships,routes) };
    }

    public SpaceProgram Restore(SaveData save) => SpaceProgram.Restore(rules,save,universe);

    SaveData BaseSave(ColonySite site, int index)
    {
        int Role(string role) => rules.Buildings.Single(building => building.SpaceRole == role).Id;
        int Art(string art) => rules.Buildings.First(building => building.Art == art).Id;
        int[] buildings = [Role("hub"), Role("relay"), Art("solar"), Art("solar"), Art("habitat"),
            Art("greenhouse"), Art("refinery"), Art("battery")];
        int[][] tiles = buildings.Select((type,i) =>
            new[] { (site.Top + i / site.Width) * Colony.Size + site.Left + i % site.Width, type }).ToArray();

        var stock = rules.Materials.ToDictionary(material => material.Id,_ => 0L);
        stock["propellant"] = index % 5 == 0 ? 0 : 10_000;
        stock["alloy"] = index % 2 == 0 ? 10_000 : 20;
        stock["water"] = index % 2 == 0 ? 20 : 10_000;
        stock["ore"] = index % 3 == 0 ? 0 : 10_000;
        stock["food"] = 2_000;
        return new(4,tiles,null,4,0,null,stock,new(),Map: new(site.Seed,site.Abundance),
            ActivatedResearch: [], LongOrbit: site.Kind == "orbit",
            PlanetBounds: site.PlanetaryEngineering ? new(site.Width,site.Height) : null);
    }
}
