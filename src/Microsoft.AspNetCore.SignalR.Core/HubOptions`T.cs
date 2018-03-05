// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;

namespace Microsoft.AspNetCore.SignalR
{
    public class HubOptions<THub> : HubOptions where THub : Hub
    {
        public new TimeSpan? NegotiateTimeout { get; set; } = null;

        public new TimeSpan? KeepAliveInterval { get; set; } = null;

        public new List<string> SupportedProtocols { get; set; } = null;
    }
}
