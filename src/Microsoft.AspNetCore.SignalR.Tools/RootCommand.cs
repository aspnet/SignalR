// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.


using Microsoft.Extensions.CommandLineUtils;

namespace Microsoft.AspNetCore.SignalR.Tools
{
    internal class RootCommand
    {
        public void Configure(CommandLineApplication command)
        {
            command.Command("ghp", new GenerateHubProxiesCommand().Configure);

            command.OnExecute(() =>
            {
                var args = command.RemainingArguments;
                if (args.Count == 0 || args[0] != "ghp")
                {
                    command.ShowHelp();
                }
                return 0;
            });
        }
    }
}
