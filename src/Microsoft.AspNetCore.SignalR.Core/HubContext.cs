// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;

namespace Microsoft.AspNetCore.SignalR
{
    public class HubContext<THub> : IHubContext<THub> where THub : Hub
    {
        private readonly HubLifetimeManager<THub> _lifetimeManager;

        public HubContext(HubLifetimeManager<THub> lifetimeManager)
        {
            _lifetimeManager = lifetimeManager;
            Groups = new GroupManager<THub>(lifetimeManager);
        }

        public IHubClients Clients => new HubClients<THub>(_lifetimeManager);

        public virtual IGroupManager Groups { get; }
    }
}
