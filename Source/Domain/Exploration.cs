namespace RedMeridian.Domain;

sealed record Exploration(string Base, int Index, string Target, bool Galaxy, int TicksLeft);
sealed record SurveyOption(int Sols, int Cost, bool Available);
sealed record SurveyProgress(bool Galaxy, int TicksLeft);
sealed record TelescopeStatus(int Level, int Range, bool Online, SurveyProgress? Survey, SurveyOption Systems, SurveyOption Galaxies);

sealed partial class Colony
{
    public int TelescopeLevel(int index) => BuildingAt(index) is > 0 and var type &&
        rules.Buildings[type - 1].SpaceRole == "telescope" ? Level(index) : 0;

    public bool TelescopeOnline(int index) => TelescopeLevel(index) > 0 && Group(index).IsRunning(index);
    public int TelescopeAnchor(int tile) => surface.AnchorAt(tile);
}

sealed partial class SpaceProgram
{
    const int SystemSurveySols = 8;
    const int GalaxySurveySols = 30;
    const int SystemSurveyCost = 300;
    const int GalaxySurveyCost = 2000;
    const int GalaxySurveyLevel = 3;
    static readonly int[] TelescopeRanges = [0, 600, 18_000, 540_000, 16_200_000];
    public static int MaxTelescopeLevel => TelescopeRanges.Length - 1;
    public static int TelescopeRange(int level) => TelescopeRanges[level];
    readonly HashSet<string> discovered = new() { StarChart.HomeSystem };
    readonly Dictionary<(string Base, int Index),Exploration> explorations = new();

    IEnumerable<ColonySite> KnownSites => universe.Sites.Where(site => discovered.Contains(site.System));
    bool KnownGalaxy(string galaxy) => galaxy == StarChart.HomeGalaxy || discovered.Contains(galaxy + "-primary");

    string? SurveyTarget(Colony colony, int level, bool galaxy)
    {
        var origin = universe.System(colony.Site.System);
        var reserved = explorations.Values.Select(survey => survey.Target).ToHashSet();
        var candidates = galaxy
            ? universe.Galaxies.Where(g => !KnownGalaxy(g.Id))
                .Select(g => (Id: g.Id + "-primary", g.X, g.Y))
            : universe.Systems.Where(system => system.Galaxy == origin.Galaxy && !discovered.Contains(system.Id))
                .Select(system => (system.Id, system.X, system.Y));
        return candidates.Where(candidate => !reserved.Contains(candidate.Id))
            .Select(candidate => (candidate.Id, Distance: StarChart.Distance(origin.X,origin.Y,candidate.X,candidate.Y)))
            .Where(candidate => candidate.Distance <= TelescopeRanges[level])
            .OrderBy(candidate => candidate.Distance).Select(candidate => candidate.Id).FirstOrDefault();
    }

    public TelescopeStatus? InspectTelescope(Colony colony, int tile)
    {
        int index = colony.TelescopeAnchor(tile);
        int level = colony.TelescopeLevel(index);
        if (level == 0) return null;
        var active = explorations.GetValueOrDefault((colony.Site.Id,index));
        return new(level, TelescopeRanges[level], colony.TelescopeOnline(index),
            active is null ? null : new(active.Galaxy,active.TicksLeft),
            new(SystemSurveySols,SystemSurveyCost,SurveyTarget(colony,level,false) is not null),
            new(GalaxySurveySols,GalaxySurveyCost,level >= GalaxySurveyLevel && SurveyTarget(colony,level,true) is not null));
    }

    public void StartSurvey(Colony colony, int tile, bool galaxy)
    {
        int index = colony.TelescopeAnchor(tile);
        int level = colony.TelescopeLevel(index);
        var key = (colony.Site.Id,index);
        if (!colony.TelescopeOnline(index) || explorations.ContainsKey(key))
            throw new InvalidOperationException("Select an operating telescope with no survey in progress.");
        if (galaxy && level < GalaxySurveyLevel)
            throw new InvalidOperationException($"Galaxy surveys require a level {GalaxySurveyLevel} or higher telescope.");
        string target = SurveyTarget(colony,level,galaxy)
            ?? throw new InvalidOperationException("No undiscovered targets are in range.");
        colony.Withdraw(new() { [rules.World.ResearchMaterial] = galaxy ? GalaxySurveyCost : SystemSurveyCost });
        int ticks = (galaxy ? GalaxySurveySols : SystemSurveySols) * StarChart.TicksPerSol;
        explorations.Add(key,new(colony.Site.Id,index,target,galaxy,ticks));
    }

    public void PruneSurveys()
    {
        foreach (var (key,survey) in explorations.ToArray())
            if (Base(survey.Base).TelescopeLevel(survey.Index) == 0) explorations.Remove(key);
    }

    void AdvanceSurveys()
    {
        PruneSurveys();
        foreach (var (key,survey) in explorations.ToArray())
        {
            if (!Base(survey.Base).TelescopeOnline(survey.Index)) continue;
            if (survey.TicksLeft > 1) explorations[key] = survey with { TicksLeft = survey.TicksLeft - 1 };
            else
            {
                discovered.Add(survey.Target);
                explorations.Remove(key);
            }
        }
    }

    void RestoreDiscoveries(string[]? systems)
    {
        foreach (string id in systems ?? [])
        {
            if (!universe.Systems.Any(system => system.Id == id) || !discovered.Add(id))
                throw new InvalidOperationException("Invalid discovered system in save.");
        }
        if (universe.Systems.Any(system => discovered.Contains(system.Id) && !KnownGalaxy(system.Galaxy)))
            throw new InvalidOperationException("A discovered system is missing its galaxy.");
    }

    void RestoreSurveys(Exploration[]? saved)
    {
        var targets = new HashSet<string>();
        foreach (var survey in saved ?? [])
        {
            if (survey is null || survey.Base is null || !bases.TryGetValue(survey.Base,out var colony) ||
                !universe.Systems.Any(system => system.Id == survey.Target) ||
                !targets.Add(survey.Target) || discovered.Contains(survey.Target) ||
                survey.TicksLeft < 1 || survey.TicksLeft > (survey.Galaxy ? GalaxySurveySols : SystemSurveySols) * StarChart.TicksPerSol)
                throw new InvalidOperationException("Invalid survey in save.");
            int level = colony.TelescopeLevel(survey.Index);
            var origin = universe.System(colony.Site.System);
            var target = universe.System(survey.Target);
            if (level == 0 || StarChart.Distance(origin.X,origin.Y,target.X,target.Y) > TelescopeRanges[level] ||
                (survey.Galaxy ? level < GalaxySurveyLevel || KnownGalaxy(target.Galaxy) || target.Id != target.Galaxy + "-primary"
                    : target.Galaxy != origin.Galaxy) ||
                !explorations.TryAdd((survey.Base,survey.Index),survey))
                throw new InvalidOperationException("Invalid telescope or survey target in save.");
        }
    }
}
