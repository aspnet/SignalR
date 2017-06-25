﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.Sockets
{
    public interface IConnection
    {
        string ConnectionId { get; }

        Task StartAsync();
        Task SendAsync(byte[] data, CancellationToken cancellationToken);
        Task DisposeAsync();

        event Func<Task> Connected;
        event Func<byte[], Task> Received;
        event Func<Exception, Task> Closed;
    }
}
