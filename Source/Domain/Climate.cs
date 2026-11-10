namespace RedMeridian.Domain;

sealed record ClimateSettings(int BaseTemperature, int Humidity)
{
    public bool Valid => BaseTemperature is >= -200 and <= 200 && Humidity is >= 0 and <= 100;
}

sealed record ClimateProfile(int BaseTemperature, int Humidity, int OffsetSpan, int Attenuation,
    int GreenhouseStart, int GreenhouseMax, int FreezeAt, int DryAt)
{
    public ClimateSettings Initial => new(BaseTemperature,Humidity);
    public bool Valid => Initial.Valid && OffsetSpan is > 0 and <= 200 &&
        Attenuation is >= 0 and <= 500 && GreenhouseStart is >= 0 and < 100 &&
        GreenhouseMax is >= 0 and <= 100 && FreezeAt >= -200 && FreezeAt < DryAt && DryAt <= 200;

    public int EffectiveBase(ClimateSettings settings) => settings.BaseTemperature * 10 +
        Math.Max(0,settings.Humidity-GreenhouseStart) * GreenhouseMax * 10 / (100-GreenhouseStart);

    public int AdjustOffset(int offset, int humidity) =>
        offset * Math.Max(0,10_000-Attenuation*humidity) / 10_000;
}

enum WaterState : byte { Land, Frozen, Liquid, Dry }

sealed class TileClimate(ColonySite site, int seed, ClimateProfile profile, ClimateSettings settings)
{
    public ClimateSettings Settings { get; set; } = settings;
    public int EffectiveBase => profile.EffectiveBase(Settings);
    public int Sunlight(int index)
    {
        int x = index % Colony.Size - site.Left;
        return Math.Clamp((x * 120 / (site.Width-1))-10 + Noise(index,0x4c957f2d,9)-4,0,100);
    }

    public int Elevation(int index)
    {
        int x = index % Colony.Size, y = index / Colony.Size;
        // A stable landing shelf leaves the first habitat and port above the shoreline.
        int shelfDistance = Math.Max(0,Math.Abs(x-511)-2) + Math.Max(0,Math.Abs(y-511)-2);
        if (shelfDistance == 0) return 8;
        if (site.Id == "terminator" && site.Width <= 30)
        {
            // The maximum slope of these waves is below two levels per tile.
            double relief = 7.5 + 5.8*Math.Sin((y-site.Top)*.16 + (seed & 255)) +
                2.2*Math.Sin((x-site.Left)*.31 + ((seed >> 8) & 255)) +
                .7*Math.Sin(((x-site.Left)+(y-site.Top))*.46 + ((seed >> 16) & 255));
            int rise = Math.Min(8,shelfDistance)*2;
            return Math.Clamp((int)Math.Round(relief),Math.Max(0,8-rise),Math.Min(15,8+rise));
        }
        int band = (int)Math.Round(7.5 + 4.5*Math.Sin((y-site.Top)*.17 + (seed & 255)) +
            2*Math.Sin((x-site.Left)*.13 + ((seed >> 8) & 255)));
        return Math.Clamp(band+Noise(index,0x165667b1,5)-2,0,15);
    }

    public int Temperature(int index) => Temperature(index,Settings);
    public int Temperature(int index, ClimateSettings current)
    {
        int x = index % Colony.Size - site.Left;
        int offset = (x*2*profile.OffsetSpan*10 / (site.Width-1))-profile.OffsetSpan*10 +
            (Noise(index,0x27d4eb2d,15)-7)*10;
        if (site.Id == "terminator" && site.Width <= 30)
        {
            int fromEdge = Math.Min(x,site.Width-1-x);
            if (fromEdge < 5)
                offset = (x < 5 ? -1 : 1) * profile.OffsetSpan * (50-fromEdge) / 5;
        }
        return profile.EffectiveBase(current)+profile.AdjustOffset(offset,current.Humidity);
    }

    public WaterState Water(int elevation,int temperature) =>
        elevation is < 1 or > 6 ? WaterState.Land :
        temperature <= profile.FreezeAt*10 ? WaterState.Frozen :
        temperature >= profile.DryAt*10 ? WaterState.Dry : WaterState.Liquid;

    public bool Buildable(int index)
    {
        int elevation = Elevation(index);
        return elevation is > 0 and < 15 &&
            (elevation >= 6 || Water(elevation,Temperature(index)) != WaterState.Liquid);
    }

    int Noise(int index,uint salt,int range)
    {
        uint hash = unchecked((uint)index*0x9e3779b9u ^ (uint)seed ^ salt);
        hash = unchecked((hash ^ (hash >> 16))*0x7feb352du);
        return (int)((hash ^ (hash >> 15)) % (uint)range);
    }
}

sealed partial class Colony
{
    bool TerrainAllows(int index,Footprint shape,bool restoring = false)
    {
        if (climate is null) return true;
        int low = 15, high = 0;
        foreach (int tile in Surface.Tiles(index,shape))
        {
            if (!climate.Buildable(tile)) return false;
            int elevation = climate.Elevation(tile);
            low = Math.Min(low,elevation);
            high = Math.Max(high,elevation);
        }
        return restoring || high-low <= 2;
    }

    bool TemperatureAllows(int index,Building building,Footprint shape)
    {
        if (climate is null || building.MinimumTemperature is null && building.MaximumTemperature is null) return true;
        int min = (building.MinimumTemperature ?? -500)*10, max = (building.MaximumTemperature ?? 500)*10;
        return Surface.Tiles(index,shape).All(tile =>
        {
            int temperature = climate.Temperature(tile);
            return temperature >= min && temperature <= max;
        });
    }

    IEnumerable<int> PlayableTiles()
    {
        for (int y = Site.Top; y < Site.Top+Site.Height; y++)
        for (int x = Site.Left; x < Site.Left+Site.Width; x++)
            yield return y*Size+x;
    }

    public byte[] ClimateMap()
    {
        if (climate is null) return [];
        var data = new byte[Site.Width*Site.Height*5];
        int offset = 0;
        foreach (int index in PlayableTiles())
        {
            int elevation = climate.Elevation(index), temperature = climate.Temperature(index);
            data[offset] = (byte)elevation;
            data[offset+1] = (byte)climate.Sunlight(index);
            System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(offset+2,2),(short)temperature);
            data[offset+4] = (byte)climate.Water(elevation,temperature);
            offset += 5;
        }
        return data;
    }

    public int ChangeClimate(ClimateSettings settings)
    {
        if (climate is null || !settings.Valid) throw new InvalidOperationException("Invalid map temperature or humidity.");
        if (settings == climate.Settings) return 0;
        var flooded = new HashSet<int>();
        foreach (int index in PlayableTiles())
        {
            int anchor = surface.AnchorAt(index);
            if (anchor < 0) continue;
            int elevation = climate.Elevation(index);
            if (climate.Water(elevation,climate.Temperature(index)) != WaterState.Liquid &&
                climate.Water(elevation,climate.Temperature(index,settings)) == WaterState.Liquid)
                flooded.Add(anchor);
        }
        foreach (int anchor in flooded) Demolish(anchor,null);
        climate.Settings = settings;
        foreach (int index in PlayableTiles())
        {
            if (Grid[index] == 0) continue;
            var building = rules.Buildings[Grid[index]-1];
            bool safe = TemperatureAllows(index,building,new(Widths[index],Heights[index]));
            if (safe && climateBlocked.Remove(index) && !disabled.Contains(index)) Group(index).Add(index);
            if (!safe && climateBlocked.Add(index) && !disabled.Contains(index)) Group(index).Remove(index);
        }
        RefreshCapacity();
        RefreshInitialOutput();
        return flooded.Count;
    }
}
