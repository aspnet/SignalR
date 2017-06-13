using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.AspNetCore.SignalR
{
    public class HubContext<THub, TClient> : IHubContext<THub, TClient>
    {
        public HubContext(HubLifetimeManager<THub, TClient> hubLifetimeManager)
        {
            if (typeof(TClient) == typeof(object))
            {
                Clients = (IHubConnectionContext<TClient>)new DynamicHubContext<THub, TClient>(hubLifetimeManager);
            }
            else
            {
                Clients = (IHubConnectionContext<TClient>)new HubContext<THub>(hubLifetimeManager as HubLifetimeManager<THub, IClientProxy>);
            }
        }

        public IHubConnectionContext<TClient> Clients { get; }
    }

    public class DynamicHubContext<THub, T> : IHubContext<THub, object>, IHubConnectionContext<object>
    {
        private readonly HubLifetimeManager<THub, T> _lifetimeManager;
        private readonly AllClientProxy<THub, T> _all;

        public DynamicHubContext(HubLifetimeManager<THub, T> lifetimeManager)
        {
            _lifetimeManager = lifetimeManager;
            _all = new AllClientProxy<THub, T>(_lifetimeManager);
        }
        
        public virtual dynamic All => _all;

        public IHubConnectionContext<object> Clients => this;

        public virtual dynamic Client(string connectionId)
        {
            return new SingleClientProxy<THub, T>(_lifetimeManager, connectionId);
        }

        public virtual dynamic Group(string groupName)
        {
            return new GroupProxy<THub, T>(_lifetimeManager, groupName);
        }

        public virtual dynamic User(string userId)
        {
            return new UserProxy<THub, T>(_lifetimeManager, userId);
        }
    }
}
