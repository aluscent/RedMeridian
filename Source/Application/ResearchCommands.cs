using RedMeridian.Domain;

namespace RedMeridian.Application;

static class ResearchCommands
{
    public static CommandChange Activate(Colony colony, Command command, int revision)
    {
        if (command.Revision != revision) throw new InvalidOperationException("Base changed. Try again.");
        return new(colony.ActivateResearch(command.Technology), []);
    }
}
