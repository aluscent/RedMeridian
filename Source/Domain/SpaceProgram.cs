namespace RedMeridian.Domain;

sealed record SpaceSave(Dictionary<string,SaveData> Bases, Spacecraft[] Ships, CargoRoute[]? Routes = null);
sealed record Spacecraft(int Id, string Location, string? Destination = null, long ArrivalTick = 0,
    Dictionary<string,long>? Cargo = null, bool Founding = false);
sealed record CargoRoute(int Ship, string Source, string Destination, string Material, int Reserve, int Reorder);

sealed partial class Colony
{
    public long Tick => tick;
    public bool Online(string role) => consumers.Any(group => group.Building.SpaceRole == role && group.Active > 0);
    public bool CanDock => Online(Site.Kind == "orbit" ? "dock" : "terminal");
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
        reserves = rules.World.LifeSupport.ToDictionary(id => id,
            id => stock[id] == 0 ? 0L : delta[id] < 0 ? (long?)(stock[id] / -delta[id]) : null)
    };
}

sealed class SpaceProgram(GameRules rules, Colony mars)
{
    public const int Payload = 60;
    public const int FoundingAlloy = 24;
    const int MaxShips = 32;
    readonly Dictionary<string,Colony> bases = new() { [ColonySite.Mars.Id] = mars };
    readonly List<Spacecraft> ships = new();
    readonly Dictionary<int,CargoRoute> routes = new();
    public Colony Mars => mars;

    public Colony Base(string? id) => id is not null && bases.TryGetValue(id, out var colony) ? colony :
        throw new InvalidOperationException("Select an established base.");

    static ColonySite Site(string? id) => ColonySite.All.SingleOrDefault(site => site.Id == id) ??
        throw new InvalidOperationException("Select a known destination.");

    static (int Fuel, int Ticks) Leg(ColonySite from, ColonySite to)
    {
        int distance = Math.Max(1, Math.Abs(from.Distance - to.Distance));
        int lift = from.Kind is "mars" or "moon" ? (distance > 1 ? 8 : 4) : 0;
        return (2 + lift + distance * 2, 2 + distance * 4);
    }

    public object Summary() => new
    {
        tick = mars.Tick, payload = Payload, foundingAlloy = FoundingAlloy, assemblyAlloy = 16,
        sites = ColonySite.All, bases = bases.Values.Select(colony => colony.LogisticsSummary()).ToArray(),
        ships = ships.Select(ship => new
        {
            ship.Id, ship.Location, ship.Destination, ship.ArrivalTick, ship.Cargo, ship.Founding,
            waiting = ship.Destination is not null && ship.ArrivalTick <= mars.Tick && !ship.Founding
        }).ToArray(),
        routes = routes.Values.ToArray(),
        legs = ColonySite.All.SelectMany(from => ColonySite.All.Where(to => to != from).Select(to =>
        {
            var (fuel, ticks) = Leg(from, to);
            return new { from = from.Id, to = to.Id, fuel, ticks };
        })).ToArray()
    };

    public void Assemble(Colony origin)
    {
        if (!origin.Online("assembly") || !origin.CanDock)
            throw new InvalidOperationException("An operating assembly yard and cargo terminal or dock are required.");
        if (ships.Count == MaxShips) throw new InvalidOperationException("Fleet limit reached.");
        origin.Withdraw(new() { ["alloy"] = 16 });
        ships.Add(new(ships.Count + 1, origin.Site.Id));
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
        if (target == origin.Site) throw new InvalidOperationException("Choose a different destination.");
        var cargo = manifest is null ? new Dictionary<string,long>() : new(manifest);
        ValidateCargo(cargo);
        bool founding = !bases.ContainsKey(target.Id);
        if (founding && ships.Any(other => other.Founding && other.Destination == target.Id))
            throw new InvalidOperationException("A founding mission is already on its way.");
        var (fuel, ticks) = Leg(origin.Site, target);
        if (mars.Tick >= long.MaxValue - ticks) throw new InvalidOperationException("Simulation clock limit reached.");
        var cost = new Dictionary<string,long>(cargo) { ["propellant"] = cargo.GetValueOrDefault("propellant") + fuel };
        if (founding) cost["alloy"] = cargo.GetValueOrDefault("alloy") + FoundingAlloy;
        origin.Withdraw(cost);
        ships[shipId - 1] = ship with
        {
            Destination = target.Id, ArrivalTick = mars.Tick + ticks, Cargo = cargo, Founding = founding
        };
    }

    void ValidateCargo(Dictionary<string,long>? cargo)
    {
        if (cargo is null || cargo.Any(item => item.Value is <= 0 or > Payload ||
            item.Key == rules.World.ResearchMaterial || !rules.Materials.Any(m => m.Id == item.Key)) ||
            cargo.Values.Sum() > Payload)
            throw new InvalidOperationException($"Cargo must contain at most {Payload} units of physical materials.");
    }

    public void SetRoute(CargoRoute route)
    {
        if (route.Ship <= 0 || route.Ship > ships.Count || route.Source is null || route.Destination is null ||
            route.Source == route.Destination ||
            !bases.ContainsKey(route.Source) || !bases.ContainsKey(route.Destination) ||
            route.Reserve < 0 || route.Reorder <= 0 ||
            route.Material == rules.World.ResearchMaterial || !rules.Materials.Any(m => m.Id == route.Material))
            throw new InvalidOperationException("Select a ship, two established bases, a material, and valid stock thresholds.");
        var ship = ships[route.Ship - 1];
        if ((ship.Destination ?? ship.Location) != route.Source && (ship.Destination ?? ship.Location) != route.Destination)
            throw new InvalidOperationException("The spacecraft must be at or heading to a route endpoint.");
        routes[route.Ship] = route;
    }

    public void ClearRoute(int ship) => routes.Remove(ship);

    public void Step()
    {
        foreach (var colony in bases.Values) colony.Step();
        for (int i = 0; i < ships.Count; i++)
        {
            var ship = ships[i];
            if (ship.Destination is null || ship.ArrivalTick > mars.Tick) continue;
            if (ship.Founding) bases.Add(ship.Destination, Found(Site(ship.Destination)));
            var target = bases[ship.Destination];
            if ((!ship.Founding && !target.CanDock) || !target.CanReceive(ship.Cargo!)) continue;
            target.Receive(ship.Cargo!);
            ships[i] = new(ship.Id, ship.Destination);
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
            int fuel = route.Material == "propellant" ? Leg(source.Site, target.Site).Fuel : 0;
            long quantity = Math.Min(Payload, Math.Min(need, source.Stock(route.Material) - route.Reserve - fuel));
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
        int BuildingId(string role) => rules.Buildings.Single(b => b.SpaceRole == role).Id;
        int center = 511 * Colony.Size + 511;
        int solar = rules.Buildings.First(b => b.Art == "solar").Id;
        int habitat = rules.Buildings.First(b => b.Art == "habitat").Id;
        int dock = BuildingId(site.Kind == "orbit" ? "dock" : "terminal");
        var stock = rules.Materials.ToDictionary(m => m.Id, _ => 0L);
        return Colony.Restore(rules, new(4, [[center, solar], [center+1, habitat],
            [center+Colony.Size, dock], [center+Colony.Size+1, BuildingId("relay")]], null, 2, mars.Tick, null,
            stock, new(), Map: new(site.Seed, site.Abundance), ActivatedResearch: []), site);
    }

    public SaveData Export() => mars.Export() with
    {
        Space = new(bases.Where(item => item.Key != ColonySite.Mars.Id)
            .ToDictionary(item => item.Key, item => item.Value.Export()), ships.ToArray(), routes.Values.ToArray())
    };

    public static SpaceProgram Restore(GameRules rules, SaveData? save)
    {
        var program = new SpaceProgram(rules, Colony.Restore(rules, save));
        if (save!.Space is not { } space) return program;
        if (save.Version != 4 || space.Bases is null || space.Ships is null ||
            space.Bases.Count >= ColonySite.All.Length || space.Ships.Length > MaxShips)
            throw new InvalidOperationException("Invalid space program save.");
        foreach (var (id, colonySave) in space.Bases)
        {
            if (id == ColonySite.Mars.Id || colonySave is null || colonySave.Version != 4 ||
                colonySave.Tick != save.Tick || colonySave.Space is not null)
                throw new InvalidOperationException("Invalid remote base save.");
            program.bases.Add(id, Colony.Restore(rules, colonySave, Site(id)));
        }
        var founding = new HashSet<string>();
        foreach (var ship in space.Ships)
        {
            if (ship is null || ship.Id != program.ships.Count + 1 || ship.Location is null ||
                !program.bases.ContainsKey(ship.Location))
                throw new InvalidOperationException("Invalid spacecraft save.");
            if (ship.Destination is { } destination)
            {
                Site(destination);
                program.ValidateCargo(ship.Cargo);
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
            program.SetRoute(route);
        }
        return program;
    }
}
