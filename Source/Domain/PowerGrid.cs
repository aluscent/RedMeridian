namespace RedMeridian.Domain;

// One pooled grid. Discharge is limited both by the stored charge and by installed inverter capacity.
sealed class PowerGrid
{
    public long Charge { get; private set; }
    public long Capacity { get; private set; }
    public long ChargeRate { get; private set; }
    public long DischargeRate { get; private set; }

    public void Configure(ProductionGroup[] batteries)
    {
        Capacity = batteries.Sum(g => (long)g.Building.Storage * g.Count);
        ChargeRate = batteries.Sum(g => (long)g.Building.ChargeRate * g.Count);
        DischargeRate = batteries.Sum(g => (long)g.Building.DischargeRate * g.Count);
        Restore(Charge);
    }

    public long Available => Math.Min(Charge,DischargeRate);
    public void Restore(long value) => Charge = Math.Clamp(value,0,Capacity);
    public void Settle(long generated, long used)
    {
        if (generated >= used) Charge = Math.Min(Capacity, Charge + Math.Min(generated - used, ChargeRate));
        else Charge -= used - generated;
    }
}
