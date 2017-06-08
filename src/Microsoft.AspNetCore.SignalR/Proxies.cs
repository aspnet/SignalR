// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Dynamic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Sockets;

namespace Microsoft.AspNetCore.SignalR
{
    public class UserProxy<THub, TClient> : DynamicObject, IClientProxy
    {
        private readonly string _userId;
        private readonly HubLifetimeManager<THub, TClient> _lifetimeManager;

        public UserProxy(HubLifetimeManager<THub, TClient> lifetimeManager, string userId)
        {
            _lifetimeManager = lifetimeManager;
            _userId = userId;
        }

        public override bool TryInvokeMember(InvokeMemberBinder binder, object[] args, out object result)
        {
            result = InvokeAsync(binder.Name, args);
            return true;
        }

        public Task InvokeAsync(string method, params object[] args)
        {
            return _lifetimeManager.InvokeUserAsync(_userId, method, args);
        }
    }

    public class GroupProxy<THub, TClient> : DynamicObject, IClientProxy
    {
        private readonly string _groupName;
        private readonly HubLifetimeManager<THub, TClient> _lifetimeManager;

        public GroupProxy(HubLifetimeManager<THub, TClient> lifetimeManager, string groupName)
        {
            _lifetimeManager = lifetimeManager;
            _groupName = groupName;
        }

        public override bool TryInvokeMember(InvokeMemberBinder binder, object[] args, out object result)
        {
            result = InvokeAsync(binder.Name, args);
            return true;
        }

        public Task InvokeAsync(string method, params object[] args)
        {
            return _lifetimeManager.InvokeGroupAsync(_groupName, method, args);
        }
    }

    public class AllClientProxy<THub, TClient> : DynamicObject, IClientProxy
    {
        private readonly HubLifetimeManager<THub, TClient> _lifetimeManager;

        public AllClientProxy(HubLifetimeManager<THub, TClient> lifetimeManager)
        {
            _lifetimeManager = lifetimeManager;
        }
        public override bool TryInvokeMember(InvokeMemberBinder binder, object[] args, out object result)
        {
            result = InvokeAsync(binder.Name, args);
            return true;
        }

        public Task InvokeAsync(string method, params object[] args)
        {
            return _lifetimeManager.InvokeAllAsync(method, args);
        }
    }

    public class SingleClientProxy<THub, TClient> : DynamicObject, IClientProxy
    {
        private readonly string _connectionId;
        private readonly HubLifetimeManager<THub, TClient> _lifetimeManager;


        public SingleClientProxy(HubLifetimeManager<THub, TClient> lifetimeManager, string connectionId)
        {
            _lifetimeManager = lifetimeManager;
            _connectionId = connectionId;
        }

        public override bool TryInvokeMember(InvokeMemberBinder binder, object[] args, out object result)
        {
            result = InvokeAsync(binder.Name, args);
            return true;
        }

        public Task InvokeAsync(string method, params object[] args)
        {
            return _lifetimeManager.InvokeConnectionAsync(_connectionId, method, args);
        }
    }

    public class GroupManager<THub, TClient> : IGroupManager
    {
        private readonly ConnectionContext _connection;
        private readonly HubLifetimeManager<THub, TClient> _lifetimeManager;

        public GroupManager(ConnectionContext connection, HubLifetimeManager<THub, TClient> lifetimeManager)
        {
            _connection = connection;
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
