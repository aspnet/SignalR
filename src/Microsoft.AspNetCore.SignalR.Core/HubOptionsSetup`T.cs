﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Extensions.Options;
using System.Collections.Generic;

namespace Microsoft.AspNetCore.SignalR
{
    public class HubOptionsSetup<THub> : IConfigureOptions<HubOptions<THub>> where THub : Hub
    {
        private readonly HubOptions _hubOptions;
        public HubOptionsSetup(IOptions<HubOptions> options)
        {
            _hubOptions = options.Value;
        }

        public void Configure(HubOptions<THub> options)
        {
            if(_hubOptions.SupportedProtocols == null)
            {
                options.SupportedProtocols = new List<string>();
            }
            else
            {
                options.SupportedProtocols = _hubOptions.SupportedProtocols;
            }
            options.KeepAliveInterval = _hubOptions.KeepAliveInterval;
            options.NegotiateTimeout = _hubOptions.NegotiateTimeout;
        }
    }
}
