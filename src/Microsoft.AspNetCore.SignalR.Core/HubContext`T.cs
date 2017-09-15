// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;

namespace Microsoft.AspNetCore.SignalR
{
	public class HubContext<THub, T> : IHubContext<THub, T>, IHubClients<T>
        where THub : Hub<T>
        where T : class
    {
        private readonly HubLifetimeManager<THub> _lifetimeManager;

        public HubContext(HubLifetimeManager<THub> lifetimeManager)
        {
            _lifetimeManager = lifetimeManager;
            All = TypedClientBuilder<T>.Build(new AllClientProxy<THub>(_lifetimeManager));
            Groups = new GroupManager<THub>(lifetimeManager);
        }

        public IHubClients<T> Clients => this;

        public virtual T All { get; }

        public virtual IGroupManager Groups { get; }

        public T AllExcept(IReadOnlyList<string> excludedIds)
        {
            return TypedClientBuilder<T>.Build(new AllClientsExceptProxy<THub>(_lifetimeManager, excludedIds));
        }

        public virtual T Client(string connectionId)
        {
            return TypedClientBuilder<T>.Build(new SingleClientProxy<THub>(_lifetimeManager, connectionId));
        }

        public virtual T Group(string groupName)
        {
            return TypedClientBuilder<T>.Build(new GroupProxy<THub>(_lifetimeManager, groupName));
        }

        public virtual T User(string userId)
        {
            return TypedClientBuilder<T>.Build(new UserProxy<THub>(_lifetimeManager, userId));
        }
    }
}
