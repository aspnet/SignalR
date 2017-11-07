// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.SignalR
{
    public class UserProxy<THub> : IClientProxy
    {
        private readonly string _userId;
        private readonly HubLifetimeManager<THub> _lifetimeManager;

        public UserProxy(HubLifetimeManager<THub> lifetimeManager, string userId)
        {
            _lifetimeManager = lifetimeManager;
            _userId = userId;
        }

        public Task InvokeAsync(string method, params object[] args)
        {
            return _lifetimeManager.InvokeUserAsync(_userId, method, args);
        }
    }

    public class GroupProxy<THub> : IClientProxy
    {
        private readonly string _groupName;
        private readonly HubLifetimeManager<THub> _lifetimeManager;

        public GroupProxy(HubLifetimeManager<THub> lifetimeManager, string groupName)
        {
            _lifetimeManager = lifetimeManager;
            _groupName = groupName;
        }

        public Task InvokeAsync(string method, params object[] args)
        {
            return _lifetimeManager.InvokeGroupAsync(_groupName, method, args);
        }
    }

    public class AllClientProxy<THub> : IClientProxy
    {
        private readonly HubLifetimeManager<THub> _lifetimeManager;

        public AllClientProxy(HubLifetimeManager<THub> lifetimeManager)
        {
            _lifetimeManager = lifetimeManager;
        }

        public Task InvokeAsync(string method, params object[] args)
        {
            return _lifetimeManager.InvokeAllAsync(method, args);
        }
    }

    public class AllClientsExceptProxy<THub> : IClientProxy
    {
        private readonly HubLifetimeManager<THub> _lifetimeManager;
        private string[] _excludedIds;

        public AllClientsExceptProxy(HubLifetimeManager<THub> lifetimeManager, params string[] excludedIds)
        {
            _lifetimeManager = lifetimeManager;
            _excludedIds = excludedIds;
        }

        public Task InvokeAsync(string method, params object[] args)
        {
            return _lifetimeManager.InvokeAllExceptAsync(method, args, _excludedIds);
        }
    }

    public class SingleClientProxy<THub> : IClientProxy
    {
        private readonly string _connectionId;
        private readonly HubLifetimeManager<THub> _lifetimeManager;

        public SingleClientProxy(HubLifetimeManager<THub> lifetimeManager, string connectionId)
        {
            _lifetimeManager = lifetimeManager;
            _connectionId = connectionId;
        }

        public Task InvokeAsync(string method, params object[] args)
        {
            return _lifetimeManager.InvokeConnectionAsync(_connectionId, method, args);
        }
    }

    public class GroupManager<THub> : IGroupManager
    {
        private readonly HubLifetimeManager<THub> _lifetimeManager;

        public GroupManager(HubLifetimeManager<THub> lifetimeManager)
        {
            _lifetimeManager = lifetimeManager;
        }

        public Task AddAsync(string connectionId, string groupName)
        {
            return _lifetimeManager.AddGroupAsync(connectionId, groupName);
        }

        public Task RemoveAsync(string connectionId, string groupName)
        {
            return _lifetimeManager.RemoveGroupAsync(connectionId, groupName);
        }
    }
}
