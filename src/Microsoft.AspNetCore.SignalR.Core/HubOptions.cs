// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;

namespace Microsoft.AspNetCore.SignalR
{
    public class HubOptions
    {
        public TimeSpan? NegotiateTimeout { get; set; } = null;

        public TimeSpan? KeepAliveInterval { get; set; } = null;

        public List<string> SupportedProtocols { get; set; } = null;
    }
}
