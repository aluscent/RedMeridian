namespace RedMeridian.Domain;

sealed partial class Colony
{
    int Level(int index) => levels.GetValueOrDefault(index,1);
    ProductionGroup Group(int index) => groups[Grid[index] - 1][Level(index) - 1];

    void Place(int index,int type,Footprint shape,int level = 1,bool off = false)
    {
        Grid[index] = (byte)type;
        counts[type - 1]++;
        surface.Occupy(index,shape);
        if (level > 1) levels.Add(index,level);
        if (off) disabled.Add(index);
        else Group(index).Add(index);
    }

    public (string Message, TileEdit Edit) Build(int index,int type)
    {
        if ((uint)index >= Grid.Length || type < 1 || type > counts.Length)
            throw new InvalidOperationException("Choose a valid tile and building.");
        var b = rules.Buildings[type - 1];
        if (!Site.Allows(b)) throw new InvalidOperationException("This facility cannot operate in this environment.");
        if (!Site.Contains(index,b.Footprint) || !surface.Open(index,b.Footprint))
            throw new InvalidOperationException("The building footprint is blocked or outside the map.");
        if (!MatchesDeposit(index,b))
            throw new InvalidOperationException($"The anchor requires {rules.World.Deposits.Single(d => d.Id == b.Deposit).Name}.");
        CheckBuild(b,1);
        Pay(b.Cost,1);
        Place(index,type,b.Footprint);
        RefreshCapacity();
        return ($"{b.Name} built.",Edit(index,type,b.Footprint));
    }

    public (string Message, TileEdit Edit) Remove(int tile)
    {
        int index = RequireStructure(tile);
        var b = rules.Buildings[Grid[index] - 1];
        var salvage = new Dictionary<string,long>();
        Demolish(index,salvage);
        RefreshCapacity();
        return ($"{b.Name} demolished. {SalvageMessage(salvage)}",new(index,0));
    }

    static TileEdit Edit(int index,int type,Footprint shape) => new(index,type,shape.Width,shape.Height);

    public (string Message, TileEdit[] Edits) EditArea(int start,int end,int type)
    {
        if ((uint)start >= Grid.Length || (uint)end >= Grid.Length || type < 0 || type > counts.Length)
            throw new InvalidOperationException("Choose two valid corners and an area tool.");
        int x0 = Math.Min(start % Size,end % Size), x1 = Math.Max(start % Size,end % Size);
        int y0 = Math.Min(start / Size,end / Size), y1 = Math.Max(start / Size,end / Size);
        int area = (x1 - x0 + 1) * (y1 - y0 + 1);
        if (area > 256) throw new InvalidOperationException("Select an area of 256 tiles or fewer.");

        var b = type == 0 ? null : rules.Buildings[type - 1];
        if (b is not null && !Site.Allows(b)) throw new InvalidOperationException("This facility cannot operate in this environment.");
        var edits = new List<TileEdit>(area);
        var claimed = new HashSet<int>();
        for (int y = y0; y <= y1; y++)
        for (int x = x0; x <= x1; x++)
        {
            int index = y * Size + x;
            if (b is null)
            {
                int owner = surface.AnchorAt(index);
                if (owner >= 0 && claimed.Add(owner)) edits.Add(new(owner,0));
            }
            else if (x + b.Footprint.Width - 1 <= x1 && y + b.Footprint.Height - 1 <= y1 &&
                Site.Contains(index,b.Footprint) && surface.Open(index,b.Footprint) && MatchesDeposit(index,b) &&
                Surface.Tiles(index,b.Footprint).All(tile => !claimed.Contains(tile)))
            {
                edits.Add(Edit(index,type,b.Footprint));
                claimed.UnionWith(Surface.Tiles(index,b.Footprint));
            }
        }
        if (edits.Count == 0)
            throw new InvalidOperationException(b is null ? "No structures to demolish." : "No eligible footprints in this area.");
        if (b is not null)
        {
            CheckBuild(b,edits.Count);
            Pay(b.Cost,edits.Count);
        }

        var salvage = new Dictionary<string,long>();
        foreach (var edit in edits)
        {
            if (b is null) Demolish(edit.Index,salvage);
            else Place(edit.Index,type,b.Footprint);
        }
        RefreshCapacity();
        string message = b is null ? $"Demolished {edits.Count} structures. {SalvageMessage(salvage)}" :
            $"Built {edits.Count} × {b.Name} across {claimed.Count} tiles.";
        return (message,edits.ToArray());
    }

    public object? Inspect(int tile)
    {
        int index = surface.AnchorAt(tile);
        if (index < 0) return null;
        int type = Grid[index], level = Level(index);
        var group = Group(index);
        var recipe = group.Building;
        bool off = disabled.Contains(index);
        bool running = !off && group.IsRunning(index);
        return new
        {
            index, type, level, disabled = off, running, workers = running ? recipe.Crew : 0,
            crew = recipe.Crew, powerOutput = running ? OutputPower(recipe) : 0,
            ratedPowerOutput = OutputPower(recipe), powerUse = running ? recipe.Power : 0,
            ratedPowerUse = recipe.Power, outputs = recipe.Outputs,
            width = (int)Widths[index], height = (int)Heights[index],
            upgrade = level <= recipe.Upgrades.Length ? recipe.Upgrades[level - 1] : null,
            weatherForecast = weatherForecasts.GetValueOrDefault(index)
        };
    }

    public string Toggle(int tile)
    {
        int index = RequireStructure(tile);
        bool enabled = disabled.Remove(index);
        if (enabled) Group(index).Add(index);
        else
        {
            Group(index).Remove(index);
            disabled.Add(index);
        }
        RefreshCapacity();
        return $"{Group(index).Building.Name} {(enabled ? "enabled" : "shut down")}.";
    }

    public string Upgrade(int tile)
    {
        int index = RequireStructure(tile);
        var b = rules.Buildings[Grid[index] - 1];
        int level = Level(index);
        if (level > b.Upgrades.Length) throw new InvalidOperationException("This building has no further upgrades.");
        var upgrade = b.Upgrades[level - 1];
        CheckCost(upgrade.Cost,1,$"{b.Name} level {level + 1}");
        Pay(upgrade.Cost,1);
        bool enabled = !disabled.Contains(index);
        if (enabled) Group(index).Remove(index);
        levels[index] = level + 1;
        if (enabled) Group(index).Add(index);
        RefreshCapacity();
        return $"{b.Name} upgraded to level {level + 1}.";
    }

    int RequireStructure(int tile)
    {
        int index = surface.AnchorAt(tile);
        if (index < 0) throw new InvalidOperationException("Select an occupied tile.");
        return index;
    }

    bool MatchesDeposit(int index,Building b) => b.Deposit is null || surface.DepositAt(index) == b.Deposit;

    void CheckBuild(Building b,int count)
    {
        if (b.Research is not null && !activatedResearch.Contains(b.Research))
            throw new InvalidOperationException($"Activate {rules.Technology(b.Research).Name} in Research milestones first.");
        CheckCost(b.Cost,count,b.Name);
    }

    void CheckCost(Dictionary<string,int> cost,int count,string name)
    {
        var missing = cost.Where(item => stock[item.Key] < (long)item.Value * count)
            .Select(item => $"{item.Value * count} {rules.Materials.Single(m => m.Id == item.Key).Name}");
        var shortages = string.Join(", ",missing);
        if (shortages.Length != 0) throw new InvalidOperationException($"Need {shortages} for {count} × {name}.");
    }

    void Pay(Dictionary<string,int> cost,int count)
    {
        foreach (var (id,amount) in cost) stock[id] -= (long)amount * count;
    }

    void Demolish(int index,Dictionary<string,long> salvage)
    {
        int i = Grid[index] - 1;
        if (!disabled.Remove(index)) Group(index).Remove(index);
        levels.Remove(index);
        weatherForecasts.Remove(index);
        Grid[index] = 0;
        counts[i]--;
        surface.Clear(index);
        foreach (var (id,amount) in rules.Buildings[i].Cost)
        {
            long recovered = Math.Min(StockLimit - stock[id],amount / 2);
            stock[id] += recovered;
            if (recovered > 0) salvage[id] = salvage.GetValueOrDefault(id) + recovered;
        }
    }

    string SalvageMessage(Dictionary<string,long> salvage) => salvage.Count == 0 ? "No materials recovered." :
        "Recovered " + string.Join(", ",salvage.Select(item =>
            $"{item.Value} {rules.Materials.Single(m => m.Id == item.Key).Name}")) + ".";
}
