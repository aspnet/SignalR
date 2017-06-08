using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.AspNetCore.SignalR
{
    class DynamicHubContext
    {
        public class HubContext<THub> : IHubContext<THub, object>, IHubConnectionContext<object>
        {
            private readonly HubLifetimeManager<THub, object> _lifetimeManager;
            private readonly AllClientProxy<THub, object> _all;

            public HubContext(HubLifetimeManager<THub, object> lifetimeManager)
            {
                _lifetimeManager = lifetimeManager;
                _all = new AllClientProxy<THub, object>(_lifetimeManager);
            }

            public IHubConnectionContext<dynamic> Clients => this;

            public virtual dynamic All => _all;

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
}
