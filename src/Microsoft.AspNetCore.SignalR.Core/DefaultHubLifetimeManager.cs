// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Internal;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.SignalR
{
    public class DefaultHubLifetimeManager<THub> : HubLifetimeManager<THub> where THub : Hub
    {
        private readonly HubConnectionStore _connections = new HubConnectionStore();
        private readonly HubGroupList _groups = new HubGroupList();
        private readonly ILogger _logger;

        public DefaultHubLifetimeManager(ILogger<DefaultHubLifetimeManager<THub>> logger)
        {
            _logger = logger;
        }

        public override Task AddGroupAsync(string connectionId, string groupName)
        {
            if (connectionId == null)
            {
                throw new ArgumentNullException(nameof(connectionId));
            }

            if (groupName == null)
            {
                throw new ArgumentNullException(nameof(groupName));
            }

            var connection = _connections[connectionId];
            if (connection == null)
            {
                return Task.CompletedTask;
            }

            _groups.Add(connection, groupName);

            return Task.CompletedTask;
        }

        public override Task RemoveGroupAsync(string connectionId, string groupName)
        {
            if (connectionId == null)
            {
                throw new ArgumentNullException(nameof(connectionId));
            }

            if (groupName == null)
            {
                throw new ArgumentNullException(nameof(groupName));
            }

            var connection = _connections[connectionId];
            if (connection == null)
            {
                return Task.CompletedTask;
            }

            _groups.Remove(connectionId, groupName);

            return Task.CompletedTask;
        }

        public override Task SendAllAsync(string methodName, object[] args)
        {
            List<Task> tasks = null;
            var message = CreateSerializedInvocationMessage(methodName, args);

            foreach (var connection in _connections)
            {
                var task = connection.WriteAsync(message);

                if (!task.IsCompletedSuccessfully)
                {
                    if (tasks == null)
                    {
                        tasks = new List<Task>();
                    }

                    tasks.Add(task.AsTask());
                }
            }

            // No async
            if (tasks == null)
            {
                return Task.CompletedTask;
            }

            // Some connections are slow
            return Task.WhenAll(tasks);
        }

        private Task SendAllWhere(string methodName, object[] args, Func<HubConnectionContext, bool> include)
        {
            List<Task> tasks = null;
            var message = CreateSerializedInvocationMessage(methodName, args);

            foreach (var connection in _connections)
            {
                if (!include(connection))
                {
                    continue;
                }

                var task = connection.WriteAsync(message);

                if (!task.IsCompletedSuccessfully)
                {
                    if (tasks == null)
                    {
                        tasks = new List<Task>();
                    }

                    tasks.Add(task.AsTask());
                }
            }

            if (tasks == null)
            {
                return Task.CompletedTask;
            }

            // Some connections are slow
            return Task.WhenAll(tasks);
        }

        public override Task SendConnectionAsync(string connectionId, string methodName, object[] args)
        {
            if (connectionId == null)
            {
                throw new ArgumentNullException(nameof(connectionId));
            }

            var connection = _connections[connectionId];

            if (connection == null)
            {
                return Task.CompletedTask;
            }

            // We're sending to a single connection
            // Write message directly to connection without caching it in memory
            var message = CreateInvocationMessage(methodName, args);

            return connection.WriteAsync(message).AsTask();
        }

        public override Task SendGroupAsync(string groupName, string methodName, object[] args)
        {
            if (groupName == null)
            {
                throw new ArgumentNullException(nameof(groupName));
            }

            var group = _groups[groupName];
            if (group != null)
            {
                // Can't optimize for sending to a single connection in a group because
                // group might be modified inbetween checking and sending
                var message = CreateSerializedInvocationMessage(methodName, args);
                var tasks = SendMessageToConnections(message, group.Values);
                return Task.WhenAll(tasks);
            }

            return Task.CompletedTask;
        }

        public override Task SendGroupsAsync(IReadOnlyList<string> groupNames, string methodName, object[] args)
        {
            // Each task represents the list of tasks for each of the writes within a group
            List<Task> tasks = null;
            var message = CreateSerializedInvocationMessage(methodName, args);

            foreach (var groupName in groupNames)
            {
                if (string.IsNullOrEmpty(groupName))
                {
                    throw new InvalidOperationException("Cannot send to an empty group name.");
                }

                var group = _groups[groupName];
                if (group != null)
                {
                    if (tasks == null)
                    {
                        tasks = new List<Task>();
                    }

                    tasks.Add(Task.WhenAll(SendMessageToConnections(message, group.Values)));
                }
            }

            return Task.WhenAll(tasks);
        }

        public override Task SendGroupExceptAsync(string groupName, string methodName, object[] args, IReadOnlyList<string> excludedIds)
        {
            if (groupName == null)
            {
                throw new ArgumentNullException(nameof(groupName));
            }

            var group = _groups[groupName];
            if (group != null)
            {
                var message = CreateSerializedInvocationMessage(methodName, args);
                var tasks = SendMessageToConnections(message, group.Values.Where(connection => !excludedIds.Contains(connection.ConnectionId)));
                return Task.WhenAll(tasks);
            }

            return Task.CompletedTask;
        }

        private IEnumerable<Task> SendMessageToConnections(SerializedHubMessage hubMessage, IEnumerable<HubConnectionContext> connectionContexts)
        {
            foreach (var connectionContext in connectionContexts)
            {
                yield return connectionContext.WriteAsync(hubMessage).AsTask();
            }
        }

        private SerializedHubMessage CreateSerializedInvocationMessage(string methodName, object[] args)
        {
            return new SerializedHubMessage(CreateInvocationMessage(methodName, args));
        }

        private HubMessage CreateInvocationMessage(string methodName, object[] args)
        {
            return new InvocationMessage(target: methodName, argumentBindingException: null, arguments: args);
        }

        public override Task SendUserAsync(string userId, string methodName, object[] args)
        {
            return SendAllWhere(methodName, args, connection =>
                string.Equals(connection.UserIdentifier, userId, StringComparison.Ordinal));
        }

        public override Task OnConnectedAsync(HubConnectionContext connection)
        {
            _connections.Add(connection);
            return Task.CompletedTask;
        }

        public override Task OnDisconnectedAsync(HubConnectionContext connection)
        {
            _connections.Remove(connection);
            _groups.RemoveDisconnectedConnection(connection.ConnectionId);
            return Task.CompletedTask;
        }

        public override Task SendAllExceptAsync(string methodName, object[] args, IReadOnlyList<string> excludedIds)
        {
            return SendAllWhere(methodName, args, connection => !excludedIds.Contains(connection.ConnectionId));
        }

        public override Task SendConnectionsAsync(IReadOnlyList<string> connectionIds, string methodName, object[] args)
        {
            return SendAllWhere(methodName, args, connection => connectionIds.Contains(connection.ConnectionId));
        }

        public override Task SendUsersAsync(IReadOnlyList<string> userIds, string methodName, object[] args)
        {
            return SendAllWhere(methodName, args, connection => userIds.Contains(connection.UserIdentifier));
        }
    }
}
