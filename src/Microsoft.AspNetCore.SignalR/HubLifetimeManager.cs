// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Channels;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Microsoft.AspNetCore.Sockets;

namespace Microsoft.AspNetCore.SignalR
{
    public abstract class HubLifetimeManager<THub>
    {
        public abstract Task OnConnectedAsync(ConnectionContext connection);

        public abstract Task OnDisconnectedAsync(ConnectionContext connection);

        public abstract Task InvokeAllAsync(string methodName, object[] args);

        // public abstract IHubProxy GetAllProxy();

        public abstract IHubClientProxy GetConnectionProxy(string connectionId);

        // public abstract IHubProxy GetGroupProxy(string groupName);

        // public abstract IHubProxy GetUserProxy(string userId);

        // public abstract IGroupManager GetGroupManager();

        public abstract Task InvokeGroupAsync(string groupName, string methodName, object[] args);

        public abstract Task InvokeUserAsync(string userId, string methodName, object[] args);

        public abstract Task AddGroupAsync(string connectionId, string groupName);

        public abstract Task RemoveGroupAsync(string connnectionId, string groupName);
    }

}
