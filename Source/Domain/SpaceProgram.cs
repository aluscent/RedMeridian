namespace RedMeridian.Domain;

sealed record SpaceSave(Dictionary<string,SaveData> Bases, Spacecraft[] Ships, CargoRoute[]? Routes = null,
    string[]? Discovered = null, Exploration[]? Surveys = null);
sealed record Spacecraft(int Id, string Location, string? Destination = null, long ArrivalTick = 0,
    Dictionary<string,long>? Cargo = null, bool Founding = false, string Type = GameRules.LegacyShipType);
sealed record AssemblyOrder(int Index, string Type, int TicksLeft);
sealed record CargoRoute(int Ship, string Source, string Destination, string Material, int Reserve, int Reorder);

sealed partial class Colony
{
    readonly Dictionary<int,AssemblyOrder> assemblyOrders = new();
    public int PendingShips => assemblyOrders.Count;
    bool IsAssemblyYard(int index) => (uint)index < Grid.Length && Grid[index] > 0 &&
        rules.Buildings[Grid[index] - 1].SpaceRole == "assembly";

    public void StartAssembly(int tile, ShipType type)
    {
        if (!HasResearch(type.Research))
            throw new InvalidOperationException($"Activate {rules.Technology(type.Research!).Name} to build this spacecraft.");
        int index = RequireStructure(tile);
        if (!IsAssemblyYard(index) || !Group(index).IsRunning(index) || !CanDock)
            throw new InvalidOperationException("An operating assembly yard and cargo terminal or dock are required.");
        if (assemblyOrders.ContainsKey(index)) throw new InvalidOperationException("This yard is already assembling a spacecraft.");
        Withdraw(type.Cost.ToDictionary(item => item.Key, item => (long)item.Value));
        assemblyOrders.Add(index, new(index, type.Id, type.BuildTicks));
    }

    public IEnumerable<string> FinishAssembly()
    {
        foreach (var order in assemblyOrders.Values.ToArray())
        {
            if (!CanDock || !Group(order.Index).IsRunning(order.Index)) continue;
            if (order.TicksLeft > 1)
                assemblyOrders[order.Index] = order with { TicksLeft = order.TicksLeft - 1 };
            else
            {
                assemblyOrders.Remove(order.Index);
                yield return order.Type;
            }
        }
    }

    public long Tick => tick;
    public bool Online(string role) => consumers.Any(group => group.Building.SpaceRole == role && group.Active > 0);
    public bool CanDock => Online(Site.Kind == "orbit" ? "dock" : "terminal") ||
        consumers.Any(group => group.Building.SpaceRole == "hub" && group.Count > 0);
    public long Stock(string material) => stock.GetValueOrDefault(material);
    public bool CanReceive(Dictionary<string,long> cargo) =>
        cargo.All(item => stock.ContainsKey(item.Key) && item.Value <= StockLimit - stock[item.Key]);

    public void Receive(Dictionary<string,long> cargo)
    {
        foreach (var (id, amount) in cargo)
            stock[id] += amount;
    }

    public void Withdraw(Dictionary<string,long> cost)
    {
        if (cost.Any(item => Stock(item.Key) < item.Value))
            throw new InvalidOperationException("Insufficient stock for cargo, construction, or launch fuel.");
        foreach (var (id, amount) in cost) stock[id] -= amount;
    }

    public object LogisticsSummary() => new
    {
        Site.Id, stock = new Dictionary<string,long>(stock), delta = new Dictionary<string,long>(delta),
        dock = CanDock, relay = Online("relay"), assembly = Online("assembly"),
        orders = assemblyOrders.Values.Select(order => new { order.Type, order.Index, order.TicksLeft,
            running = CanDock && Group(order.Index).IsRunning(order.Index) }).ToArray(),
        reserves = rules.World.LifeSupport.ToDictionary(id => id,
            id => stock[id] == 0 ? 0L : delta[id] < 0 ? (long?)(stock[id] / -delta[id]) : null)
    };
}

sealed partial class SpaceProgram(GameRules rules, Colony mars)
{
    public const int FoundingAlloy = 24;
    const int MaxShips = 32;
    readonly Dictionary<string,Colony> bases = new() { [ColonySite.Mars.Id] = mars };
    readonly List<Spacecraft> ships = new();
    readonly Dictionary<int,CargoRoute> routes = new();
    public Colony Mars => mars;
    public string[] VisitedGalaxies => bases.Values.Select(colony => colony.Site.Galaxy).Distinct().ToArray();
    public bool MaterialAvailable(string id) => rules.Materials.Any(material => material.Id == id &&
        (material.Galaxy is null || bases.Values.Any(colony => colony.Site.Galaxy == material.Galaxy)));

    public Colony Base(string? id) => id is not null && bases.TryGetValue(id, out var colony) ? colony :
        throw new InvalidOperationException("Select an established base.");

    ColonySite Site(string? id) => KnownSites.SingleOrDefault(site => site.Id == id) ??
        throw new InvalidOperationException("Select a known destination.");

    static int LaunchCost(ColonySite from,int distance) =>
        from.PlanetaryEngineering || from.Kind == "moon" ? (distance > 1 ? 8 : 4) : 0;

    static (int Fuel, int Ticks) Leg(ColonySite from, ColonySite to, ShipType type)
    {
        int distance = Distance(from,to);
        int fuel = 2 + LaunchCost(from,distance) + (distance * 2 + type.FuelDistance - 1) / type.FuelDistance;
        return ((fuel * type.FuelPercent + 99) / 100,
            2 + (distance * StarChart.TicksPerSol + type.Speed - 1) / type.Speed);
    }

    static int Distance(ColonySite from,ColonySite to)
    {
        if (from.System == to.System) return Math.Max(1,Math.Abs(from.Distance-to.Distance));
        var origin = StarChart.System(from.System);
        var target = StarChart.System(to.System);
        return StarChart.Distance(origin.X,origin.Y,target.X,target.Y);
    }

    public object Summary(string? origin = null) => new
    {
        tick = mars.Tick, foundingAlloy = FoundingAlloy,
        sites = KnownSites.Select(site => bases.TryGetValue(site.Id,out var colony) ? colony.Site : rules.NewSite(site)).ToArray(),
        systems = StarChart.Systems.Where(system => discovered.Contains(system.Id)).ToArray(),
        galaxies = StarChart.Galaxies.Where(galaxy => KnownGalaxy(galaxy.Id))
            .Select(galaxy => new { galaxy.Id, galaxy.Name, galaxy.X, galaxy.Y, galaxy.Diameter }).ToArray(),
        bases = bases.Values.Select(colony => colony.LogisticsSummary()).ToArray(),
        ships = ships.Select(ship => new
        {
            ship.Id, ship.Type, ship.Location, ship.Destination, ship.ArrivalTick, ship.Cargo, ship.Founding,
            waiting = ship.Destination is not null && ship.ArrivalTick <= mars.Tick && !ship.Founding
        }).ToArray(),
        routes = routes.Values.ToArray(),
        legs = KnownSites.Where(site => origin is null || site.Id == origin)
            .SelectMany(from => KnownSites.Where(to => to != from).Select(to =>
        {
            int distance = Distance(from,to);
            return new { from = from.Id, to = to.Id, distance, flights = rules.ShipTypes
                .Where(type => distance <= type.Range)
                .Select(type => { var (fuel,ticks) = Leg(from,to,type); return new { type = type.Id, fuel, ticks }; })
                .ToArray() };
        })).ToArray()
    };

    public void Assemble(Colony origin, int index, string? type)
    {
        if (ships.Count + bases.Values.Sum(colony => colony.PendingShips) >= MaxShips)
            throw new InvalidOperationException("Fleet limit reached.");
        origin.StartAssembly(index, rules.ShipType(type));
    }

    public void Dispatch(Colony origin, int shipId, string? destination, Dictionary<string,long>? manifest, bool automatic = false)
    {
        if (!origin.CanDock || !origin.Online("relay"))
            throw new InvalidOperationException("An operating communications relay and cargo terminal or dock are required.");
        if (shipId <= 0 || shipId > ships.Count) throw new InvalidOperationException("Select a spacecraft.");
        var ship = ships[shipId - 1];
        if (ship.Location != origin.Site.Id || ship.Destination is not null)
            throw new InvalidOperationException("The spacecraft is not idle at this base.");
        if (!automatic && routes.ContainsKey(shipId))
            throw new InvalidOperationException("Clear this spacecraft's recurring route before manual dispatch.");
        var target = Site(destination);
        if (target.Id == origin.Site.Id) throw new InvalidOperationException("Choose a different destination.");
        var cargo = manifest is null ? new Dictionary<string,long>() : new(manifest);
        var type = rules.ShipType(ship.Type);
        if (Distance(origin.Site,target) > type.Range)
            throw new InvalidOperationException("This destination exceeds the spacecraft's travel range.");
        ValidateCargo(cargo,type.Payload);
        if (cargo.Keys.Any(id => !MaterialAvailable(id)))
            throw new InvalidOperationException("Cargo contains an unavailable material.");
        bool founding = !bases.ContainsKey(target.Id);
        if (founding && ships.Any(other => other.Founding && other.Destination == target.Id))
            throw new InvalidOperationException("A founding mission is already on its way.");
        var (fuel, ticks) = Leg(origin.Site, target,type);
        if (mars.Tick >= long.MaxValue - ticks) throw new InvalidOperationException("Simulation clock limit reached.");
        var cost = new Dictionary<string,long>(cargo) { ["propellant"] = cargo.GetValueOrDefault("propellant") + fuel };
        if (founding) cost["alloy"] = cargo.GetValueOrDefault("alloy") + FoundingAlloy;
        origin.Withdraw(cost);
        ships[shipId - 1] = ship with
        {
            Destination = target.Id, ArrivalTick = mars.Tick + ticks, Cargo = cargo, Founding = founding
        };
    }

    void ValidateCargo(Dictionary<string,long>? cargo,int payload)
    {
        if (cargo is null || cargo.Any(item => item.Value <= 0 || item.Value > payload ||
            item.Key == rules.World.ResearchMaterial || !rules.Materials.Any(m => m.Id == item.Key)) ||
            cargo.Values.Sum() > payload)
            throw new InvalidOperationException($"Cargo must contain at most {payload} units of physical materials.");
    }

    public void SetRoute(CargoRoute route, bool restoring = false)
    {
        if (route.Ship <= 0 || route.Ship > ships.Count || route.Source is null || route.Destination is null ||
            route.Source == route.Destination ||
            !bases.ContainsKey(route.Source) || !bases.ContainsKey(route.Destination) ||
            route.Reserve < 0 || route.Reorder <= 0 ||
            route.Material == rules.World.ResearchMaterial || !rules.Materials.Any(m => m.Id == route.Material))
            throw new InvalidOperationException("Select a ship, two established bases, a material, and valid stock thresholds.");
        var ship = ships[route.Ship - 1];
        if (!restoring && !MaterialAvailable(route.Material))
            throw new InvalidOperationException("Select an available route material.");
        if (Distance(bases[route.Source].Site,bases[route.Destination].Site) > rules.ShipType(ship.Type).Range)
            throw new InvalidOperationException("This route exceeds the spacecraft's travel range.");
        if ((ship.Destination ?? ship.Location) != route.Source && (ship.Destination ?? ship.Location) != route.Destination)
            throw new InvalidOperationException("The spacecraft must be at or heading to a route endpoint.");
        routes[route.Ship] = route;
    }

    public void ClearRoute(int ship) => routes.Remove(ship);

    public void Step()
    {
        foreach (var colony in bases.Values)
        {
            colony.Step();
            foreach (string type in colony.FinishAssembly())
                ships.Add(new(ships.Count + 1, colony.Site.Id, Type: type));
        }
        AdvanceSurveys();
        for (int i = 0; i < ships.Count; i++)
        {
            var ship = ships[i];
            if (ship.Destination is null || ship.ArrivalTick > mars.Tick) continue;
            if (ship.Founding) bases.Add(ship.Destination, Found(Site(ship.Destination)));
            var target = bases[ship.Destination];
            if ((!ship.Founding && !target.CanDock) || !target.CanReceive(ship.Cargo!)) continue;
            target.Receive(ship.Cargo!);
            ships[i] = new(ship.Id, ship.Destination, Type: ship.Type);
        }
        foreach (var route in routes.Values.OrderBy(route => route.Ship))
        {
            var ship = ships[route.Ship - 1];
            if (ship.Destination is not null) continue;
            var source = bases[route.Source];
            var target = bases[route.Destination];
            long incoming = ships.Where(other => other.Destination == route.Destination)
                .Sum(other => other.Cargo!.GetValueOrDefault(route.Material));
            long need = route.Reorder - target.Stock(route.Material) - incoming;
            var type = rules.ShipType(ship.Type);
            int fuel = route.Material == "propellant" ? Leg(source.Site,target.Site,type).Fuel : 0;
            long quantity = Math.Min(type.Payload, Math.Min(need, source.Stock(route.Material) - route.Reserve - fuel));
            if (quantity <= 0) continue;
            try
            {
                if (ship.Location == route.Destination) Dispatch(target, ship.Id, route.Source, null, automatic: true);
                else Dispatch(source, ship.Id, route.Destination, new() { [route.Material] = quantity }, automatic: true);
            }
            catch (InvalidOperationException) { /* Recheck power, fuel, and availability next tick. */ }
        }
    }

    Colony Found(ColonySite site)
    {
        site = rules.NewSite(site);
        int center = 511 * Colony.Size + 511;
        int hub = rules.Buildings.Single(b => b.SpaceRole == "hub").Id;
        var stock = rules.Materials.ToDictionary(m => m.Id, _ => 0L);
        var colony = Colony.Restore(rules, new(4, [[center, hub]], null, 2, mars.Tick, null,
            stock, new(), Map: new(site.Seed, site.Abundance, DensityPercent: site.Id == "terminator"),
            ActivatedResearch: [], LongOrbit: site.Kind == "orbit",
            PlanetBounds: site.PlanetaryEngineering ? new(site.Width,site.Height) : null), site);
        ShareGalaxyResearch(colony);
        return colony;
    }

    public SaveData Export() => mars.Export() with
    {
        Space = new(bases.Where(item => item.Key != ColonySite.Mars.Id)
            .ToDictionary(item => item.Key, item => item.Value.Export()), ships.ToArray(), routes.Values.ToArray(),
            discovered.Where(id => id != StarChart.HomeSystem).Order().ToArray(), explorations.Values.ToArray())
    };

    void ShareGalaxyResearch(Colony colony) =>
        bases.Values.FirstOrDefault(other => other != colony && other.Site.Galaxy == colony.Site.Galaxy)?.ShareResearchWith(colony);

    public static SpaceProgram Restore(GameRules rules, SaveData? save)
    {
        var program = new SpaceProgram(rules, Colony.Restore(rules, save));
        if (save!.Space is not { } space) return program;
        if (save.Version != 4 || space.Bases is null || space.Ships is null ||
            space.Bases.Count >= ColonySite.All.Length || space.Ships.Length > MaxShips)
            throw new InvalidOperationException("Invalid space program save.");
        program.RestoreDiscoveries(space.Discovered);
        foreach (var (id, colonySave) in space.Bases)
        {
            if (id == ColonySite.Mars.Id || colonySave is null || colonySave.Version != 4 ||
                colonySave.Tick != save.Tick || colonySave.Space is not null)
                throw new InvalidOperationException("Invalid remote base save.");
            var colony = Colony.Restore(rules, colonySave, program.Site(id));
            program.bases.Add(id, colony);
            program.ShareGalaxyResearch(colony);
        }
        program.RestoreSurveys(space.Surveys);
        if (space.Ships.Length + program.bases.Values.Sum(colony => colony.PendingShips) > MaxShips)
            throw new InvalidOperationException("Fleet and assembly orders exceed the fleet limit.");
        var founding = new HashSet<string>();
        foreach (var ship in space.Ships)
        {
            if (ship is null || ship.Id != program.ships.Count + 1 || ship.Location is null ||
                !program.bases.ContainsKey(ship.Location))
                throw new InvalidOperationException("Invalid spacecraft save.");
            var type = rules.ShipType(ship.Type);
            if (ship.Destination is { } destination)
            {
                program.Site(destination);
                program.ValidateCargo(ship.Cargo,type.Payload);
                if (destination == ship.Location || ship.ArrivalTick <= 0 ||
                    ship.Founding == program.bases.ContainsKey(destination) ||
                    ship.Founding && (!founding.Add(destination) || ship.ArrivalTick <= save.Tick))
                    throw new InvalidOperationException("Invalid shipment save.");
            }
            else if (ship.Founding || ship.ArrivalTick != 0 || ship.Cargo is not null)
                throw new InvalidOperationException("Invalid idle spacecraft save.");
            program.ships.Add(ship);
        }
        foreach (var route in space.Routes ?? [])
        {
            if (route is null || program.routes.ContainsKey(route.Ship))
                throw new InvalidOperationException("Invalid recurring route save.");
            program.SetRoute(route,restoring: true);
        }
        return program;
    }
}
