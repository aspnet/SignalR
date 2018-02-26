// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Microsoft.Extensions.Options;

namespace Microsoft.AspNetCore.SignalR
{
    public class HubOptionsSetup : IConfigureOptions<HubOptions>
    {
        private readonly List<string> _protocols = new List<string>();

        public HubOptionsSetup(IEnumerable<IHubProtocol> protocols)
        {
            foreach (IHubProtocol hubProtocol in protocols)
            {
                _protocols.Add(hubProtocol.Name);
            }
        }

        public void Configure(HubOptions options)
        {
            if (options.SupportedProtocols == null)
            {
                options.SupportedProtocols = new List<string>();
            }
            options.SupportedProtocols.AddRange(_protocols);
        }
    }
}

