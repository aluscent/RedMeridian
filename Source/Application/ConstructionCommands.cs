using RedMeridian.Domain;

namespace RedMeridian.Application;

static class ConstructionCommands
{
    public static CommandChange Apply(Colony colony, Command command, int revision)
    {
        if (command.Revision != revision)
            throw new InvalidOperationException("Map changed. Refreshing the sector.");

        return command.Action switch
        {
            "build" => One(colony.Build(command.Index, command.Type)),
            "remove" => One(colony.Remove(command.Index)),
            "toggle" => new(colony.Toggle(command.Index), []),
            "upgrade" => new(colony.Upgrade(command.Index), []),
            "area" => Area(colony.EditArea(command.Index, command.EndIndex, command.Type)),
            _ => throw new InvalidOperationException("Unknown construction command.")
        };
    }

    static CommandChange One((string Message, TileEdit Edit) change) => new(change.Message, [change.Edit]);
    static CommandChange Area((string Message, TileEdit[] Edits) change) => new(change.Message, change.Edits);
}
