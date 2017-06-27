// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Extensions.CommandLineUtils;

namespace Microsoft.AspNetCore.SignalR.Tools
{
    internal class GenerateHubProxiesCommand
    {
        private CommandOption _path;

        public void Configure(CommandLineApplication command)
        {
            command.Description = "Generate hub proxies";
            _path = command.Option("-p|--path <Assembly>", "Path to the assembly to generate proxies from", CommandOptionType.SingleValue);

            command.OnExecute(() =>
            {
                using (var hubDiscovery = new HubDiscovery(_path.Value()))
                {
                    var proxies = hubDiscovery.GetHubProxies();
                    // TODO: Write proxies
                    // TODO: Handle exceptions
                }

                return 0;
            });
        }
    }
}