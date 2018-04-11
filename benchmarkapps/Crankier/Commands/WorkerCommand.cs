// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.CommandLineUtils;

using static Microsoft.AspNetCore.SignalR.Crankier.Commands.CommandLineUtilities;

namespace Microsoft.AspNetCore.SignalR.Crankier.Commands
{
    internal class WorkerCommand
    {
        public static void Register(CommandLineApplication app)
        {
            app.Command("worker", cmd =>
            {
                var agentOption = cmd.Option("--agent <PARENT_PID>", "The process ID of the agent controlling this worker", CommandOptionType.SingleValue);
                var waitForDebuggerOption = cmd.Option("--wait-for-debugger", "Provide this flag to have the worker wait for the debugger.", CommandOptionType.NoValue);

                cmd.OnExecute(async () =>
                {
                    if (!agentOption.HasValue())
                    {
                        return MissingRequiredArg(agentOption);
                    }

                    if (!int.TryParse(agentOption.Value(), out var agentPid))
                    {
                        return InvalidArg(agentOption);
                    }

                    if (waitForDebuggerOption.HasValue())
                    {
                        SpinWait.SpinUntil(() => Debugger.IsAttached);
                    }

                    return await Execute(agentPid);
                });
            });
        }

        private static async Task<int> Execute(int agentPid)
        {
            try
            {
                var worker = new Worker(agentPid);
                await worker.RunAsync();
            }
            catch (Exception ex)
            {
                return Fail(ex.ToString());
            }

            return 0;
        }
    }
}
