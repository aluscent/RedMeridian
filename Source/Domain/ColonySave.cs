namespace RedMeridian.Domain;

sealed partial class Colony
{
    static readonly string[] LegacyMaterials = ["ore","water","food","alloy","research"];
    static readonly string[] LegacyMetrics = ["temperature","pressure","oxygen","surface-water"];

    public SaveData Export()
    {
        var tiles = new List<int[]>(counts.Sum());
        var structures = new List<SavedStructure>();
        for (int y = Site.Top; y < Site.Top + Site.Height; y++)
        for (int x = Site.Left; x < Site.Left + Site.Width; x++)
        {
            int index = y * Size + x;
            if (Grid[index] == 0) continue;
            tiles.Add([index,Grid[index]]);
            int level = Level(index);
            bool off = disabled.Contains(index);
            if (Widths[index] != 1 || Heights[index] != 1 || level != 1 || off)
                structures.Add(new(index,Widths[index],Heights[index],level,off));
        }
        return new(4,tiles.ToArray(),null,population,tick,null,new(stock),new(planet),power.Charge,MapSettings,structures.ToArray(),
            weatherForecasts.Count == 0 ? null : new(weatherForecasts), ActivatedResearch: activatedResearch.ToArray(),
            LongOrbit: Site.Kind == "orbit" && Site.Height == 150, Assembly: assemblyOrders.Values.ToArray(),
            PlanetBounds: Site.PlanetaryEngineering && Site.Width != Size ? new(Site.Width,Site.Height) : null);
    }

    public static Colony Restore(GameRules rules,SaveData? save,ColonySite? site = null)
    {
        if (save is null || save.Version is not (1 or 2 or 3 or 4) || save.Tiles is null ||
            save.Tiles.Length > Size * Size || save.Population is < 0 or > MaxPopulation ||
            save.Tick is < 0 or >= long.MaxValue || save.Charge < 0)
            throw new InvalidOperationException("Invalid colony save.");
        if (site?.Kind == "orbit" && !save.LongOrbit) site = site with { Width = 16, Length = null };
        if (save.PlanetBounds is { } bounds)
        {
            site ??= ColonySite.Mars;
            if (!site.PlanetaryEngineering || !bounds.Valid)
                throw new InvalidOperationException("Invalid saved planet size.");
            site = site with { Width = bounds.Width, Length = bounds.Height };
        }
        var colony = new Colony(rules,map: save.Version == 4 ? save.Map : null,empty: true,site: site)
            { population = save.Population, tick = save.Tick };
        var structures = new Dictionary<int,SavedStructure>();
        if (save.Version == 4 && save.Structures is not null)
        {
            foreach (var details in save.Structures)
                if (details is null || !structures.TryAdd(details.Index,details))
                    throw new InvalidOperationException("Duplicate or invalid structure metadata.");
        }
        int maxType = Math.Min(rules.Buildings.Length,save.Version == 1 ? 6 : save.Version == 2 ? 10 : byte.MaxValue);
        foreach (var tile in save.Tiles)
        {
            if (tile is not { Length: 2 } || (uint)tile[0] >= colony.Grid.Length ||
                tile[1] < 1 || tile[1] > maxType || colony.Grid[tile[0]] != 0)
                throw new InvalidOperationException("Invalid or unknown structure in colony save.");
            int index = tile[0], type = tile[1];
            var details = structures.Remove(index,out var saved) ? saved : new SavedStructure(index);
            var shape = new Footprint(details.Width,details.Height);
            if (!colony.Site.Contains(index,shape) || !colony.Site.Allows(rules.Buildings[type - 1], restoring: true) ||
                !colony.surface.Open(index,shape) || details.Level < 1 || details.Level > colony.groups[type - 1].Length)
                throw new InvalidOperationException("Invalid footprint, overlap or upgrade level in save.");
            colony.Place(index,type,shape,details.Level,details.Disabled);
        }

        if (structures.Count > 0) throw new InvalidOperationException("Structure metadata has no matching tile.");
        foreach (var order in save.Assembly ?? [])
        {
            if (order is null || !colony.IsAssemblyYard(order.Index) || order.TicksLeft <= 0 ||
                order.TicksLeft > 100_000 || !colony.assemblyOrders.TryAdd(order.Index,order))
                throw new InvalidOperationException("Invalid spacecraft assembly order.");
            rules.ShipType(order.Type);
        }
        if (save.Version == 4 && save.WeatherForecasts is not null)
        {
            foreach (var (index, forecast) in save.WeatherForecasts)
            {
                if ((uint)index >= colony.Grid.Length || colony.Grid[index] == 0 ||
                    !rules.Buildings[colony.Grid[index] - 1].ForecastsWeather ||
                    forecast is null || forecast.ObservedAt < 0 || forecast.ObservedAt > save.Tick ||
                    forecast.StartsInMin < 1 || forecast.StartsInMax < forecast.StartsInMin ||
                    forecast.DurationMin < 1 || forecast.DurationMax < forecast.DurationMin ||
                    forecast.StormSolarOutput < 0 || forecast.Demand < 0 || forecast.BatteryTicks < 0)
                    throw new InvalidOperationException("Invalid weather observation in colony save.");
                colony.weatherForecasts.Add(index, forecast);
            }
        }

        void Material(string id,long amount)
        {
            if (!colony.stock.ContainsKey(id) || amount is < 0 or > StockLimit)
                throw new InvalidOperationException($"Invalid or unknown saved material: {id}.");
            colony.stock[id] = amount;
        }
        void PlanetMetric(string id,int value)
        {
            var metric = rules.World.Metrics.FirstOrDefault(m => m.Id == id);
            if (metric is null) throw new InvalidOperationException($"Unknown saved planet metric: {id}.");
            if (colony.Site.PlanetaryEngineering) colony.planet[id] = Math.Clamp(value,metric.Initial,metric.Target);
        }

        if (save.Version >= 3)
        {
            if (save.Materials is null || save.Metrics is null) throw new InvalidOperationException("Missing saved inventories or planet metrics.");
            foreach (var (id,value) in save.Materials) Material(id,value);
            foreach (var (id,value) in save.Metrics) PlanetMetric(id,value);
        }
        else
        {
            int resourceCount = save.Version == 1 ? 4 : 5;
            if (save.Stock is null || save.Stock.Length != resourceCount)
                throw new InvalidOperationException("Invalid legacy inventory.");
            for (int i = 0; i < resourceCount; i++) Material(LegacyMaterials[i],save.Stock[i]);
            if (save.Version == 2)
            {
                if (save.Planet is not { Length: 4 }) throw new InvalidOperationException("Invalid legacy planet metrics.");
                for (int i = 0; i < LegacyMetrics.Length; i++) PlanetMetric(LegacyMetrics[i],save.Planet[i]);
            }
        }
        // Before activation was introduced, meeting a threshold unlocked the milestone immediately.
        if (save.ActivatedResearch is null)
        {
            long points = colony.stock[rules.World.ResearchMaterial];
            colony.activatedResearch.UnionWith(rules.Research.Where(item => rules.Unlocked(item.Id,points)).Select(item => item.Id));
        }
        else
        {
            var activated = save.ActivatedResearch;
            if (activated.Length > rules.Research.Length || activated.Distinct().Count() != activated.Length ||
                activated.Any(id => !rules.Research.Any(item => item.Id == id)))
                throw new InvalidOperationException("Invalid activated research in colony save.");
            colony.activatedResearch.UnionWith(activated);
            if (activated.Any(id => !rules.Technology(id).Requires.All(colony.activatedResearch.Contains)))
                throw new InvalidOperationException("An activated milestone is missing a prerequisite.");
        }
        colony.housing = rules.Buildings.Sum(b => colony.counts[b.Id - 1] * b.Beds);
        colony.RefreshCapacity();
        colony.power.Restore(save.Charge);
        colony.RefreshInitialOutput();
        return colony;
    }
}
