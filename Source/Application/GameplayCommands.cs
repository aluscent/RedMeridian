using RedMeridian.Domain;

namespace RedMeridian.Application;

// One registration per feature; the HTTP session does not need to know each gameplay action.
static class GameplayCommands
{
    delegate CommandChange Handler(SpaceProgram program, Colony colony, Command command, int revision);

    static readonly Dictionary<string,Handler> handlers = new(StringComparer.Ordinal)
    {
        ["build"] = Construction,
        ["remove"] = Construction,
        ["toggle"] = Construction,
        ["upgrade"] = Construction,
        ["area"] = Construction,
        ["activate-research"] = Research,
        ["assemble"] = Fleet,
        ["dispatch"] = Fleet,
        ["explore"] = Fleet,
        ["set-route"] = Fleet,
        ["clear-route"] = Fleet
    };

    public static CommandChange Apply(SpaceProgram program, Colony colony, Command command, int revision, bool stress)
    {
        if (command.Action is null || !handlers.TryGetValue(command.Action,out var handler))
            throw new InvalidOperationException("Unknown command.");
        if (stress && handler == Fleet)
            throw new InvalidOperationException("Return to your colony to use logistics.");
        return handler(program,colony,command,revision);
    }

    static CommandChange Construction(SpaceProgram _, Colony colony, Command command, int revision) =>
        ConstructionCommands.Apply(colony,command,revision);
    static CommandChange Research(SpaceProgram _, Colony colony, Command command, int revision) =>
        ResearchCommands.Activate(colony,command,revision);
    static CommandChange Fleet(SpaceProgram program, Colony colony, Command command, int revision) =>
        FleetCommands.Apply(program,colony,command,revision);
}
