// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Microsoft.AspNetCore.SignalR
{
    public class HubContext<THub, TClient> : IHubContext<THub, TClient>
    {
        public HubContext(HubLifetimeManager<THub, TClient> hubLifetimeManager)
        {
            if (typeof(TClient) == typeof(object))
            {
                Clients = (IHubConnectionContext<TClient>)new DynamicHubContext<THub>(hubLifetimeManager as HubLifetimeManager<THub, object>);
            }
            else if(typeof(TClient) == typeof(IClientProxy))
            {
                Clients = (IHubConnectionContext<TClient>)new HubContext<THub>(hubLifetimeManager as HubLifetimeManager<THub, IClientProxy>);
            }
        }

        public IHubConnectionContext<TClient> Clients { get; }
    }

    public class DynamicHubContext<THub> : IHubContext<THub, object>, IHubConnectionContext<object>
    {
        private readonly HubLifetimeManager<THub, object> _lifetimeManager;
        private readonly AllClientProxy<THub, object> _all;

        public DynamicHubContext(HubLifetimeManager<THub, object> lifetimeManager)
        {
            _lifetimeManager = lifetimeManager;
            _all = new AllClientProxy<THub, object>(_lifetimeManager);
        }
        
        public virtual dynamic All => _all;

        public IHubConnectionContext<object> Clients => this;

        public virtual dynamic Client(string connectionId)
        {
            return new SingleClientProxy<THub, object>(_lifetimeManager, connectionId);
        }

        public virtual dynamic Group(string groupName)
        {
            return new GroupProxy<THub, object>(_lifetimeManager, groupName);
        }

        public virtual dynamic User(string userId)
        {
            return new UserProxy<THub, object>(_lifetimeManager, userId);
        }
    }
}
