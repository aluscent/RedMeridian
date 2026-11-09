namespace RedMeridian.Domain;

sealed record WeatherForecast(long ObservedAt, long StartsInMin, long StartsInMax,
    long DurationMin, long DurationMax, long StormSolarOutput, long Demand, long? BatteryTicks);

sealed partial class Colony
{
    readonly Dictionary<int,WeatherForecast> weatherForecasts = new();

    void RecordWeatherForecasts()
    {
        if (!observatories.Any(group => group.Active > 0)) return;

        var cycle = rules.World.Storm;
        long phase = tick % cycle.Period;
        long until = phase < cycle.Start ? cycle.Start - phase : cycle.Period - phase + cycle.Start;
        long duration = cycle.End - cycle.Start;
        long startError = Math.Min(12, until / 4);
        long durationError = Math.Min(4, until / 8);
        long solar = 0, steadyPower = 0;
        foreach (var group in producers)
        {
            var building = group.Building;
            if (building.Inputs.Count != 0 || building.Crew != 0) continue;
            long output = (long)group.Count * (building.StormGeneration ?? building.Generation);
            steadyPower += output;
            if (building.Art == "solar") solar += output;
        }
        long deficit = Math.Max(0, demand - steadyPower);
        long? runway = deficit == 0 ? null : deficit > power.DischargeRate ? 0 : power.Charge / deficit;
        var forecast = new WeatherForecast(tick, until - startError, until + startError,
            Math.Max(1, duration - durationError), duration + durationError, solar, demand, runway);
        foreach (var group in observatories)
        foreach (int index in group.ActiveMembers)
            weatherForecasts[index] = forecast;
    }
}
