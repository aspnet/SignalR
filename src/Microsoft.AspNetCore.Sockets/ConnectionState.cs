// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.Sockets
{
    public class ConnectionState
    {
        public Connection Connection { get; set; }

        // These are used for long polling mostly
        public Action Close { get; set; }
        public DateTimeOffset LastSeen { get; set; }
        public bool Active { get; set; }
        // This is for handling situations when send comes before poll or sse
        public TaskCompletionSource<bool> CanSend { get; } = new TaskCompletionSource<bool>();
    }
}
