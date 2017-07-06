// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Microsoft.AspNetCore.SignalR
{  
    public class HubContext<THub> : IHubContext<THub>, IHubClients where THub : Hub
    {
        private readonly HubLifetimeManager<THub> _lifetimeManager;
        private readonly AllClientProxy<THub> _all;

        public HubContext(HubLifetimeManager<THub> lifetimeManager)
        {
            _lifetimeManager = lifetimeManager;
            _all = new AllClientProxy<THub>(_lifetimeManager);
        }

        public IHubClients Clients => this;

        public virtual IClientProxy All => _all;

        public virtual IClientProxy Client(string connectionId)
        {
            return new SingleClientProxy<THub>(_lifetimeManager, connectionId);
        }

        public virtual IClientProxy Group(string groupName)
        {
            return new GroupProxy<THub>(_lifetimeManager, groupName);
        }

        public virtual IClientProxy User(string userId)
        {
            return new UserProxy<THub>(_lifetimeManager, userId);
        }
    }
}
