// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Sockets;
using Microsoft.Extensions.Internal;

namespace Microsoft.AspNetCore.SignalR
{
    public class DefaultHubLifetimeManager<THub> : HubLifetimeManager<THub>
    {
        private readonly ConcurrentDictionary<string, HubConnection> _hubConnections = new ConcurrentDictionary<string, HubConnection>();

        public override Task AddGroupAsync(HubConnection connection, string groupName)
        {
            var groups = connection.Metadata.GetOrAdd("groups", _ => new HashSet<string>());

            lock (groups)
            {
                groups.Add(groupName);
            }

            return TaskCache.CompletedTask;
        }

        public override Task RemoveGroupAsync(HubConnection connection, string groupName)
        {
            var groups = connection.Metadata.Get<HashSet<string>>("groups");

            lock (groups)
            {
                groups.Remove(groupName);
            }

            return TaskCache.CompletedTask;
        }

        public override Task InvokeAllAsync(string methodName, object[] args)
        {
            return InvokeAllWhere(methodName, args, c => true);
        }

        private Task InvokeAllWhere(string methodName, object[] args, Func<HubConnection, bool> include)
        {
            var tasks = new List<Task>(_hubConnections.Count);

            // TODO: serialize once per format by providing a different stream?
            foreach (var connection in _hubConnections)
            {
                if (!include(connection.Value))
                {
                    continue;
                }

                tasks.Add(connection.Value.InvokeAsync(methodName, args));
            }

            return Task.WhenAll(tasks);
        }

        public override Task InvokeConnectionAsync(string connectionId, string methodName, object[] args)
        {
            HubConnection hubConnection;
            if (!_hubConnections.TryGetValue(connectionId, out hubConnection))
            {
                return TaskCache.CompletedTask;
            }

            return hubConnection.InvokeAsync(methodName, args);
        }

        public override Task InvokeGroupAsync(string groupName, string methodName, object[] args)
        {
            return InvokeAllWhere(methodName, args, connection =>
            {
                var groups = connection.Metadata.Get<HashSet<string>>("groups");
                return groups?.Contains(groupName) == true;
            });
        }

        public override Task InvokeUserAsync(string userId, string methodName, object[] args)
        {
            return InvokeAllWhere(methodName, args, connection =>
            {
                return connection.User.Identity.Name == userId;
            });
        }

        public override Task OnConnectedAsync(HubConnection connection)
        {
            _hubConnections.TryAdd(connection.ConnectionId, connection);
            return TaskCache.CompletedTask;
        }

        public override Task OnDisconnectedAsync(HubConnection connection)
        {
            HubConnection ignore;
            _hubConnections.TryRemove(connection.ConnectionId, out ignore);
            return TaskCache.CompletedTask;
        }
    }

}
