using Microsoft.Extensions.CommandLineUtils;

using static Microsoft.AspNetCore.SignalR.Crankier.Commands.CommandLineUtilities;

namespace Microsoft.AspNetCore.SignalR.Crankier.Commands
{
    internal class AgentCommand
    {
        public static void Register(CommandLineApplication app)
        {
            app.Command("agent", cmd => cmd.OnExecute(() => Fail("Not yet implemented")));
        }
    }
}
