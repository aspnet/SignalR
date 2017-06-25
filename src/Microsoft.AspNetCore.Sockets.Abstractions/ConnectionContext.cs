// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Channels;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Sockets.Client;

namespace Microsoft.AspNetCore.Sockets
{
    public abstract class ConnectionContext : IConnection
    {
        public abstract string ConnectionId { get; }

        public abstract IFeatureCollection Features { get; }

        public abstract ClaimsPrincipal User { get; set; }

        // REVIEW: Should this be changed to items
        public abstract ConnectionMetadata Metadata { get; }

        public abstract Channel<byte[]> Transport { get; set; }

        public event Action Connected;
        public event Action<byte[]> Received;
        public event Action<Exception> Closed;

        Task IConnection.DisposeAsync()
        {
            Transport.Out.TryComplete();

            // REVIEW: Is this correct?
            return Transport.In.Completion;
        }

        public async Task SendAsync(byte[] data, CancellationToken cancellationToken)
        {
            while (await Transport.Out.WaitToWriteAsync(cancellationToken))
            {
                if (Transport.Out.TryWrite(data))
                {
                    break;
                }
            }
        }

        public Task StartAsync()
        {
            async Task Run()
            {
                try
                {
                    while (await Transport.In.WaitToReadAsync())
                    {
                        while (Transport.In.TryRead(out var buffer))
                        {
                            Received?.Invoke(buffer);
                        }
                    }

                    Closed?.Invoke(null);
                }
                catch (Exception ex)
                {
                    Closed?.Invoke(ex);
                }
            }

            _ = Run();

            Connected?.Invoke();

            return Task.CompletedTask;
        }
    }
}
