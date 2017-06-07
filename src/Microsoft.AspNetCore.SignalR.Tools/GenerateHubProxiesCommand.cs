// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Extensions.CommandLineUtils;

namespace Microsoft.AspNetCore.SignalR.Tools
{
    internal class GenerateHubProxiesCommand
    {
        private CommandOption _project;

        public void Configure(CommandLineApplication command)
        {
            command.Description = "Generate hub proxies";
            _project = command.Option("-p|--project <PROJECT>", "Project to generate proxies from", CommandOptionType.SingleValue);

            command.OnExecute(() => 0);
        }
    }
}
