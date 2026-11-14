using RedMeridian.Domain;

namespace RedMeridian.Application;

record Command(string? Action, int Index = -1, int Type = 0, int Revision = -1, SaveData? Save = null,
    int EndIndex = -1, MapSettings? Map = null, string? Base = null, int Ship = 0,
    Dictionary<string,long>? Cargo = null, CargoRoute? Route = null, string? Technology = null,
    string? Material = null, int Amount = 0, string? ShipType = null, ClimateSettings? Climate = null, string? SaveSlot = null,
    bool GalaxySurvey = false, string[]? Materials = null);

record CommandChange(string Message, TileEdit[] Edits);
