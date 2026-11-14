namespace RedMeridian.Domain;

readonly record struct ResourceFlow(long Produced, long Consumed, long Inbound, long Outbound)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public long NetProduction => Produced - Consumed;
}

sealed class MapResources(GameRules rules, string mapId)
{
    readonly long[] stock = rules.Materials.Select(material => material.Initial).ToArray();
    ResourceFlow[]? flows;

    public long this[string material]
    {
        get => stock[rules.MaterialSlots[material]];
        set => stock[rules.MaterialSlots[material]] = value;
    }

    public bool Contains(string material) => rules.MaterialSlots.ContainsKey(material);
    public void BeginTick()
    {
        if (flows is not null) Array.Clear(flows);
    }

    public void Produce(string material, long quantity)
    {
        int slot = rules.MaterialSlots[material];
        long stored = Math.Min(quantity,Colony.StockLimit - stock[slot]);
        if (stored == 0) return;
        stock[slot] += stored;
        ref var flow = ref Flow(slot);
        flow = flow with { Produced = flow.Produced + stored };
    }

    public void Consume(string material, long quantity)
    {
        int slot = rules.MaterialSlots[material];
        long used = Math.Min(quantity,stock[slot]);
        if (used == 0) return;
        stock[slot] -= used;
        ref var flow = ref Flow(slot);
        flow = flow with { Consumed = flow.Consumed + used };
    }

    public void RecordFreight(IReadOnlyDictionary<string,long> cargo, bool inbound)
    {
        foreach (var (material,quantity) in cargo)
        {
            ref var flow = ref Flow(rules.MaterialSlots[material]);
            flow = inbound ? flow with { Inbound = flow.Inbound + quantity } :
                flow with { Outbound = flow.Outbound + quantity };
        }
    }

    ref ResourceFlow Flow(int slot) => ref (flows ??= new ResourceFlow[stock.Length])[slot];
    public ResourceFlow FlowOf(string material) => FlowAt(rules.MaterialSlots[material]);
    ResourceFlow FlowAt(int slot) => flows?[slot] ?? default;

    public Dictionary<string,long> StockSnapshot()
    {
        var snapshot = new Dictionary<string,long>(stock.Length);
        for (int slot = 0; slot < stock.Length; slot++) snapshot.Add(rules.Materials[slot].Id,stock[slot]);
        return snapshot;
    }

    public Dictionary<string,long> DeltaSnapshot()
    {
        var snapshot = new Dictionary<string,long>(stock.Length);
        for (int slot = 0; slot < stock.Length; slot++) snapshot.Add(rules.Materials[slot].Id,FlowAt(slot).NetProduction);
        return snapshot;
    }

    public Dictionary<string,ResourceFlow>? SaveFlows()
    {
        if (flows is null) return null;
        var snapshot = new Dictionary<string,ResourceFlow>();
        for (int slot = 0; slot < stock.Length; slot++)
        {
            var flow = FlowAt(slot);
            if (flow != default) snapshot.Add(rules.Materials[slot].Id,flow);
        }
        return snapshot.Count == 0 ? null : snapshot;
    }

    public void RestoreFlows(Dictionary<string,ResourceFlow>? snapshot)
    {
        foreach (var (id,flow) in snapshot ?? [])
        {
            // Bound imported counters so adding this tick's freight cannot overflow.
            if (!rules.MaterialSlots.TryGetValue(id,out int slot) || !Valid(flow.Produced) || !Valid(flow.Consumed) ||
                !Valid(flow.Inbound) || !Valid(flow.Outbound))
                throw new InvalidOperationException("Invalid saved resource flow.");
            if (flow != default) Flow(slot) = flow;
        }

        static bool Valid(long value) => value is >= 0 and <= long.MaxValue / 2;
    }

    public object Snapshot(long tick) => new
    {
        mapId, tick,
        materials = rules.Materials.ToDictionary(m => m.Id,m =>
        {
            var flow = FlowOf(m.Id);
            return new { stock = this[m.Id], flow, netProduction = flow.NetProduction };
        })
    };
}
