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
        private event Func<Task> _connected;
        private event Func<byte[], Task> _received;
        private event Func<Exception, Task> _closed;

        public abstract string ConnectionId { get; }

        public abstract IFeatureCollection Features { get; }

        public abstract ClaimsPrincipal User { get; set; }

        // REVIEW: Should this be changed to items
        public abstract ConnectionMetadata Metadata { get; }

        public abstract Channel<byte[]> Transport { get; set; }


        event Func<Task> IConnection.Connected
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

        event Func<byte[], Task> IConnection.Received
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

        event Func<Exception, Task> IConnection.Closed
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
                            // Don't block here
                            _ = _received?.Invoke(buffer);
                        }
                    }

                    await _closed?.Invoke(null);
                }
                catch (Exception ex)
                {
                    await _closed?.Invoke(ex);
                }
            }

            _ = Run();

            return _connected?.Invoke() ?? Task.CompletedTask;
        }
    }
}
