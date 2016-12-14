// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Microsoft.AspNetCore.SignalR.Redis
{
    public class RedisHubLifetimeManager<THub> : HubLifetimeManager<THub>, IDisposable
    {
        private readonly ConcurrentDictionary<string, HubConnection> _connections = new ConcurrentDictionary<string, HubConnection>();
        // TODO: Investigate "memory leak" entries never get removed
        private readonly ConcurrentDictionary<string, GroupData> _groups = new ConcurrentDictionary<string, GroupData>();
        private readonly InvocationAdapterRegistry _registry;
        private readonly ConnectionMultiplexer _redisServerConnection;
        private readonly ISubscriber _bus;
        private readonly ILoggerFactory _loggerFactory;
        private readonly RedisOptions _options;

        public RedisHubLifetimeManager(InvocationAdapterRegistry registry,
                                       ILoggerFactory loggerFactory,
                                       IOptions<RedisOptions> options)
        {
            _loggerFactory = loggerFactory;
            _registry = registry;
            _options = options.Value;

            var writer = new LoggerTextWriter(loggerFactory.CreateLogger<RedisHubLifetimeManager<THub>>());
            _redisServerConnection = _options.Connect(writer);
            _bus = _redisServerConnection.GetSubscriber();

            var previousBroadcastTask = TaskCache.CompletedTask;

            _bus.Subscribe(typeof(THub).FullName, async (c, data) =>
            {
                await previousBroadcastTask;

                var tasks = new List<Task>(_connections.Count);

                foreach (var connection in _connections)
                {
                    tasks.Add(connection.Value.WriteAsync(data));
                }

                previousBroadcastTask = Task.WhenAll(tasks);
            });
        }

        public override Task InvokeAllAsync(string methodName, object[] args)
        {
            var message = new InvocationDescriptor
            {
                Method = methodName,
                Arguments = args
            };

            return PublishAsync(typeof(THub).FullName, message);
        }

        public override Task InvokeConnectionAsync(string connectionId, string methodName, object[] args)
        {
            var message = new InvocationDescriptor
            {
                Method = methodName,
                Arguments = args
            };

            return PublishAsync(typeof(THub).FullName + "." + connectionId, message);
        }

        public override Task InvokeGroupAsync(string groupName, string methodName, object[] args)
        {
            var message = new InvocationDescriptor
            {
                Method = methodName,
                Arguments = args
            };

            return PublishAsync(typeof(THub).FullName + ".group." + groupName, message);
        }

        public override Task InvokeUserAsync(string userId, string methodName, object[] args)
        {
            var message = new InvocationDescriptor
            {
                Method = methodName,
                Arguments = args
            };

            return PublishAsync(typeof(THub).FullName + ".user." + userId, message);
        }

        private async Task PublishAsync(string channel, InvocationDescriptor message)
        {
            // TODO: What format??
            var invocationAdapter = _registry.GetInvocationAdapter("json");

            // BAD
            using (var ms = new MemoryStream())
            {
                await invocationAdapter.WriteMessageAsync(message, ms);

                await _bus.PublishAsync(channel, ms.ToArray());
            }
        }

        public override Task OnConnectedAsync(HubConnection connection)
        {
            var redisSubscriptions = connection.Metadata.GetOrAdd("redis_subscriptions", _ => new HashSet<string>());
            var connectionTask = TaskCache.CompletedTask;
            var userTask = TaskCache.CompletedTask;

            _connections.TryAdd(connection.ConnectionId, connection);

            var connectionChannel = typeof(THub).FullName + "." + connection.ConnectionId;
            redisSubscriptions.Add(connectionChannel);

            var previousConnectionTask = TaskCache.CompletedTask;

            connectionTask = _bus.SubscribeAsync(connectionChannel, async (c, data) =>
            {
                await connection.WriteAsync(data);
            });


            if (connection.User.Identity.IsAuthenticated)
            {
                var userChannel = typeof(THub).FullName + ".user." + connection.User.Identity.Name;
                redisSubscriptions.Add(userChannel);

                var previousUserTask = TaskCache.CompletedTask;

                // TODO: Look at optimizing (looping over connections checking for Name)
                userTask = _bus.SubscribeAsync(userChannel, async (c, data) =>
                {
                    await connection.WriteAsync(data);
                });
            }

            return Task.WhenAll(connectionTask, userTask);
        }

        public override Task OnDisconnectedAsync(HubConnection connection)
        {
            HubConnection ignore;
            _connections.TryRemove(connection.ConnectionId, out ignore);

            var tasks = new List<Task>();

            var redisSubscriptions = connection.Metadata.Get<HashSet<string>>("redis_subscriptions");
            if (redisSubscriptions != null)
            {
                foreach (var subscription in redisSubscriptions)
                {
                    tasks.Add(_bus.UnsubscribeAsync(subscription));
                }
            }

            var groupNames = connection.Metadata.Get<HashSet<string>>("group");

            if (groupNames != null)
            {
                // Copy the groups to an array here because they get removed from this collection
                // in RemoveGroupAsync
                foreach (var group in groupNames.ToArray())
                {
                    tasks.Add(RemoveGroupAsync(connection, group));
                }
            }

            return Task.WhenAll(tasks);
        }

        public override async Task AddGroupAsync(HubConnection connection, string groupName)
        {
            var groupChannel = typeof(THub).FullName + ".group." + groupName;

            var groupNames = connection.Metadata.GetOrAdd("group", _ => new HashSet<string>());

            lock (groupNames)
            {
                groupNames.Add(groupName);
            }

            var group = _groups.GetOrAdd(groupChannel, _ => new GroupData());

            await group.Lock.WaitAsync();
            try
            {
                group.Connections.TryAdd(connection.ConnectionId, connection);

                // Subscribe once
                if (group.Connections.Count > 1)
                {
                    return;
                }

                var previousTask = TaskCache.CompletedTask;

                await _bus.SubscribeAsync(groupChannel, async (c, data) =>
                {
                    // Since this callback is async, we await the previous task then
                    // before sending the current message. This is because we don't
                    // want to do concurrent writes to the outgoing connections
                    await previousTask;

                    var tasks = new List<Task>(group.Connections.Count);
                    foreach (var groupConnection in group.Connections)
                    {
                        tasks.Add(groupConnection.Value.WriteAsync(data));
                    }

                    previousTask = Task.WhenAll(tasks);
                });
            }
            finally
            {
                group.Lock.Release();
            }
        }

        public override async Task RemoveGroupAsync(HubConnection connection, string groupName)
        {
            var groupChannel = typeof(THub).FullName + ".group." + groupName;

            GroupData group;
            if (!_groups.TryGetValue(groupChannel, out group))
            {
                return;
            }

            var groupNames = connection.Metadata.Get<HashSet<string>>("group");
            if (groupNames != null)
            {
                lock (groupNames)
                {
                    groupNames.Remove(groupName);
                }
            }

            await group.Lock.WaitAsync();
            try
            {
                HubConnection ignore;
                if (!group.Connections.TryRemove(connection.ConnectionId, out ignore))
                {
                    return;
                }

                if (group.Connections.Count == 0)
                {
                    await _bus.UnsubscribeAsync(groupChannel);
                }
            }
            finally
            {
                group.Lock.Release();
            }
        }

        public void Dispose()
        {
            _bus.UnsubscribeAll();
            _redisServerConnection.Dispose();
        }

        private class LoggerTextWriter : TextWriter
        {
            private readonly ILogger _logger;

            public LoggerTextWriter(ILogger logger)
            {
                _logger = logger;
            }

            public override Encoding Encoding => Encoding.UTF8;

            public override void Write(char value)
            {

            }

            public override void WriteLine(string value)
            {
                _logger.LogDebug(value);
            }
        }

        private class GroupData
        {
            public SemaphoreSlim Lock = new SemaphoreSlim(1, 1);
            public ConcurrentDictionary<string, HubConnection> Connections = new ConcurrentDictionary<string, HubConnection>();
        }
    }
}
