namespace RedMeridian.Domain;

// Wire format for legacy and current campaigns. Keep names stable for old saves.
record SaveData(int Version, int[][]? Tiles, long[]? Stock, int Population, long Tick, int[]? Planet,
    Dictionary<string,long>? Materials = null, Dictionary<string,int>? Metrics = null, long Charge = 0,
    MapSettings? Map = null, SavedStructure[]? Structures = null,
    Dictionary<int,WeatherForecast>? WeatherForecasts = null, SpaceSave? Space = null,
    string[]? ActivatedResearch = null, bool LongOrbit = false, AssemblyOrder[]? Assembly = null,
    PlanetSize? PlanetBounds = null, ClimateSettings? Climate = null, Dictionary<string,int>? EngineeringCredit = null, int MigrationCredit = 0,
    string? MapId = null, int LastBuildingId = 0, Dictionary<string,ResourceFlow>? ResourceFlows = null);
