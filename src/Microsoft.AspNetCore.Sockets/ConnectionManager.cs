// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Channels;
using Microsoft.AspNetCore.Sockets.Internal;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Sockets
{
    public class ConnectionManager
    {
        private readonly ConcurrentDictionary<string, ConnectionState> _connections = new ConcurrentDictionary<string, ConnectionState>();
        private readonly Timer _timer;
        private volatile bool _running;
        private readonly ILogger<ConnectionManager> _logger;

        public ConnectionManager(ILogger<ConnectionManager> logger)
        {
            _timer = new Timer(Scan, this, 0, 1000);
            _logger = logger;
        }

        public bool TryGetConnection(string id, out ConnectionState state)
        {
            return _connections.TryGetValue(id, out state);
        }

        public ConnectionState CreateConnection()
        {
            var id = MakeNewConnectionId();

            var transportToApplication = Channel.CreateUnbounded<Message>();
            var applicationToTransport = Channel.CreateUnbounded<Message>();

            var transportSide = new ChannelConnection<Message>(applicationToTransport, transportToApplication);
            var applicationSide = new ChannelConnection<Message>(transportToApplication, applicationToTransport);

            var state = new ConnectionState(
                new Connection(id, applicationSide),
                transportSide);

            _connections.TryAdd(id, state);
            return state;
        }

        public void RemoveConnection(string id)
        {
            ConnectionState state;
            _connections.TryRemove(id, out state);

            // Remove the connection completely
        }

        private static string MakeNewConnectionId()
        {
            // TODO: We need to sign and encyrpt this
            return Guid.NewGuid().ToString();
        }

        private static void Scan(object state)
        {
            ((ConnectionManager)state).Scan();
        }

        private void Scan()
        {
            if (_running)
            {
                return;
            }

            try
            {
                _running = true;

                // Scan the registered connections looking for ones that have timed out
                foreach (var c in _connections)
                {
                    var status = ConnectionState.ConnectionStatus.Inactive;

                    try
                    {
                        c.Value.Lock.Wait();
                        
                        // Capture the connection status
                        status = c.Value.Status;
                    }
                    finally
                    {
                        c.Value.Lock.Release();
                    }

                    // Once the decision has been made to to dispose we don't check the status again
                    if (status == ConnectionState.ConnectionStatus.Inactive && (DateTimeOffset.UtcNow - c.Value.LastSeenUtc).TotalSeconds > 5)
                    {
                        var ignore = DisposeAndRemoveAsync(c);
                    }
                }
            }
            finally
            {
                _running = false;
            }
        }

        public void CloseConnections()
        {
            // Stop firing the timer
            _timer.Dispose();

            var tasks = new List<Task>();

            foreach (var c in _connections)
            {
                tasks.Add(DisposeAndRemoveAsync(c));
            }

            Task.WaitAll(tasks.ToArray(), TimeSpan.FromSeconds(5));
        }

        private async Task DisposeAndRemoveAsync(KeyValuePair<string, ConnectionState> pair)
        {
            var state = pair.Value;

            try
            {
                await state.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(0, ex, "Failed disposing inactive connection {connectionId}", state.Connection.ConnectionId);
            }
            finally
            {
                // Remove it from the list after disposal so that's it's easy to see
                // connections that might be in a hung state via the connections list
                ConnectionState s;
                _connections.TryRemove(pair.Key, out s);

                _logger.LogDebug("Removing {connectionId} from the list of connections", state.Connection.ConnectionId);
            }
        }
    }
}
