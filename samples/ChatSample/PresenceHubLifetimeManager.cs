﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.SignalR.Redis;

namespace ChatSample
{
    public class DefaultPresenceHublifetimeManager<THub> : PresenceHubLifetimeManager<THub, DefaultHubLifetimeManager<THub>>
        where THub : HubWithPresence
    {
        public DefaultPresenceHublifetimeManager(IUserTracker<THub> userTracker, IServiceScopeFactory serviceScopeFactory,
            ILoggerFactory loggerFactory, IServiceProvider serviceProvider)
            : base(userTracker, serviceScopeFactory, loggerFactory, serviceProvider)
        {
        }
    }

    public class RedisPresenceHublifetimeManager<THub> : PresenceHubLifetimeManager<THub, RedisHubLifetimeManager<THub>>
    where THub : HubWithPresence
    {
        public RedisPresenceHublifetimeManager(IUserTracker<THub> userTracker, IServiceScopeFactory serviceScopeFactory,
            ILoggerFactory loggerFactory, IServiceProvider serviceProvider)
            : base(userTracker, serviceScopeFactory, loggerFactory, serviceProvider)
        {
        }
    }

    public class PresenceHubLifetimeManager<THub, THubLifetimeManager> : HubLifetimeManager<THub>, IDisposable
        where THubLifetimeManager : HubLifetimeManager<THub>
        where THub : HubWithPresence
    {
        private readonly HubConnectionList _connections = new HubConnectionList();
        private readonly IUserTracker<THub> _userTracker;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly ILogger _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly HubLifetimeManager<THub> _wrappedHubLifetimeManager;
        private IHubContext<THub> _hubContext;

        public PresenceHubLifetimeManager(IUserTracker<THub> userTracker, IServiceScopeFactory serviceScopeFactory,
            ILoggerFactory loggerFactory, IServiceProvider serviceProvider)
        {
            _userTracker = userTracker;
            _userTracker.UsersJoined += OnUsersJoined;
            _userTracker.UsersLeft += OnUsersLeft;

            _serviceScopeFactory = serviceScopeFactory;
            _serviceProvider = serviceProvider;
            _logger = loggerFactory.CreateLogger<PresenceHubLifetimeManager<THub, THubLifetimeManager>>();
            _wrappedHubLifetimeManager = serviceProvider.GetRequiredService<THubLifetimeManager>();
        }

        public override async Task OnConnectedAsync(HubConnectionContext connection)
        {
            await _wrappedHubLifetimeManager.OnConnectedAsync(connection);
            _connections.Add(connection);
            await _userTracker.AddUser(connection, new UserDetails(connection.ConnectionId, connection.User.Identity.Name));
        }

        public override async Task OnDisconnectedAsync(HubConnectionContext connection)
        {
            await _wrappedHubLifetimeManager.OnDisconnectedAsync(connection);
            _connections.Remove(connection);
            await _userTracker.RemoveUser(connection);
        }

        private async void OnUsersJoined(UserDetails[] users)
        {
            await Notify(hub =>
            {
                if (users.Length == 1)
                {
                    if (users[0].ConnectionId != hub.Context.ConnectionId)
                    {
                        return hub.OnUsersJoined(users);
                    }
                }
                else
                {
                    return hub.OnUsersJoined(
                        users.Where(u => u.ConnectionId != hub.Context.Connection.ConnectionId).ToArray());
                }
                return Task.CompletedTask;
            });
        }

        private async void OnUsersLeft(UserDetails[] users)
        {
            await Notify(hub => hub.OnUsersLeft(users));
        }

        private async Task Notify(Func<THub, Task> invocation)
        {
            foreach (var connection in _connections)
            {
                using (var scope = _serviceScopeFactory.CreateScope())
                {
                    var hubActivator = scope.ServiceProvider.GetRequiredService<IHubActivator<THub>>();
                    var hub = hubActivator.Create();

                    if (_hubContext == null)
                    {
                        // Cannot be injected due to circular dependency
                        _hubContext = _serviceProvider.GetRequiredService<IHubContext<THub>>();
                    }

                    hub.Clients = _hubContext.Clients;
                    hub.Context = new HubCallerContext(connection);
                    hub.Groups = _hubContext.Groups;

                    try
                    {
                        await invocation(hub);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Presence notification failed.");
                    }
                    finally
                    {
                        hubActivator.Release(hub);
                    }
                }
            }
        }

        public void Dispose()
        {
            _userTracker.UsersJoined -= OnUsersJoined;
            _userTracker.UsersLeft -= OnUsersLeft;
        }

        public override Task InvokeAllAsync(string methodName, object[] args)
        {
            return _wrappedHubLifetimeManager.InvokeAllAsync(methodName, args);
        }

        public override Task InvokeAllExceptAsync(string methodName, object[] args, params string[] excludedIds)
        {
            return _wrappedHubLifetimeManager.InvokeAllExceptAsync(methodName, args, excludedIds);
        }

        public override Task InvokeConnectionAsync(string connectionId, string methodName, object[] args)
        {
            return _wrappedHubLifetimeManager.InvokeConnectionAsync(connectionId, methodName, args);
        }

        public override Task InvokeGroupAsync(string groupName, string methodName, object[] args)
        {
            return _wrappedHubLifetimeManager.InvokeGroupAsync(groupName, methodName, args);
        }

        public override Task InvokeUserAsync(string userId, string methodName, object[] args)
        {
            return _wrappedHubLifetimeManager.InvokeUserAsync(userId, methodName, args);
        }

        public override Task AddGroupAsync(string connectionId, string groupName)
        {
            return _wrappedHubLifetimeManager.AddGroupAsync(connectionId, groupName);
        }

        public override Task RemoveGroupAsync(string connectionId, string groupName)
        {
            return _wrappedHubLifetimeManager.RemoveGroupAsync(connectionId, groupName);
        }
    }
}
