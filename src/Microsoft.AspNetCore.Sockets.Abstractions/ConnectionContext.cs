// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Channels;
using Microsoft.AspNetCore.Http.Features;

namespace Microsoft.AspNetCore.Sockets
{
    public abstract class ConnectionContext : IConnection
    {
        private event Action _connected;
        private event Action<byte[]> _received;
        private event Action<Exception> _closed;

        public abstract string ConnectionId { get; }

        public abstract IFeatureCollection Features { get; }

        public abstract ClaimsPrincipal User { get; set; }

        // REVIEW: Should this be changed to items
        public abstract ConnectionMetadata Metadata { get; }

        public abstract Channel<byte[]> Transport { get; set; }


        event Action IConnection.Connected
        {
            add
            {
                _connected += value;
            }
            remove
            {
                _connected -= value;
            }
        }

        event Action<byte[]> IConnection.Received
        {
            add
            {
                _received += value;
            }
            remove
            {
                _received -= value;
            }
        }

        event Action<Exception> IConnection.Closed
        {
            add
            {
                _closed += value;
            }
            remove
            {
                _closed -= value;
            }
        }

        Task IConnection.DisposeAsync()
        {
            Transport.Out.TryComplete();

            // REVIEW: Is this correct?
            return Transport.In.Completion;
        }

        async Task IConnection.SendAsync(byte[] data, CancellationToken cancellationToken)
        {
            while (await Transport.Out.WaitToWriteAsync(cancellationToken))
            {
                if (Transport.Out.TryWrite(data))
                {
                    break;
                }
            }
        }

        Task IConnection.StartAsync()
        {
            async Task Run()
            {
                try
                {
                    while (await Transport.In.WaitToReadAsync())
                    {
                        while (Transport.In.TryRead(out var buffer))
                        {
                            _received?.Invoke(buffer);
                        }
                    }

                    _closed?.Invoke(null);
                }
                catch (Exception ex)
                {
                    _closed?.Invoke(ex);
                }
            }

            _ = Run();

            _connected?.Invoke();

            return Task.CompletedTask;
        }
    }
}
