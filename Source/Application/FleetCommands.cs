using RedMeridian.Domain;

namespace RedMeridian.Application;

static class FleetCommands
{
    public static CommandChange Apply(SpaceProgram program, Colony current, Command command, int revision)
    {
        if (command.Revision != revision) throw new InvalidOperationException("Base changed. Try again.");
        string message = command.Action switch
        {
            "assemble" => Assemble(),
            "dispatch" => Dispatch(),
            "explore" => Explore(),
            "set-route" => SetRoute(),
            "clear-route" => ClearRoute(),
            _ => throw new InvalidOperationException("Unknown fleet command.")
        };
        return new(message, []);

        string Assemble()
        {
            program.Assemble(current, command.Index, command.ShipType);
            return "Spacecraft assembly started.";
        }
        string Dispatch()
        {
            program.Dispatch(current, command.Ship, command.Base, command.Cargo);
            return "Spacecraft launched.";
        }
        string Explore()
        {
            program.StartSurvey(current, command.Index, command.GalaxySurvey);
            return "Exploration started.";
        }
        string SetRoute()
        {
            program.SetRoute(command.Route ?? throw new InvalidOperationException("Provide a cargo route."));
            return "Recurring cargo route configured.";
        }
        string ClearRoute()
        {
            program.ClearRoute(command.Ship);
            return "Recurring route cleared.";
        }
    }
}
