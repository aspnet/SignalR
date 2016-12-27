// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.AspNetCore.Sockets;

namespace Microsoft.AspNetCore.SignalR
{
    public abstract class HubLifetimeManager<THub>
    {
        public abstract Task OnConnectedAsync(HubConnection connection);

        public abstract Task OnDisconnectedAsync(HubConnection connection);

        public abstract Task InvokeAllAsync(string methodName, object[] args);

        public abstract Task InvokeConnectionAsync(string connectionId, string methodName, object[] args);

        public abstract Task InvokeGroupAsync(string groupName, string methodName, object[] args);

        public abstract Task InvokeUserAsync(string userId, string methodName, object[] args);

        public abstract Task AddGroupAsync(HubConnection connection, string groupName);

        public abstract Task RemoveGroupAsync(HubConnection connection, string groupName);
    }

}
