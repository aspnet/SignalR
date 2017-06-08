// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.SignalR
{
    public class HubContext<THub> : IHubContext<THub, IClientProxy>, IHubConnectionContext<IClientProxy>
    {
        private readonly HubLifetimeManager<THub, IClientProxy> _lifetimeManager;
        private readonly AllClientProxy<THub, IClientProxy> _all;

        public HubContext(HubLifetimeManager<THub, IClientProxy> lifetimeManager)
        {
            _lifetimeManager = lifetimeManager;
            _all = new AllClientProxy<THub, IClientProxy>(_lifetimeManager);
        }

        public IHubConnectionContext<IClientProxy> Clients => this;

        public virtual IClientProxy All => _all;

        public virtual IClientProxy Client(string connectionId)
        {
            return new SingleClientProxy<THub, IClientProxy>(_lifetimeManager, connectionId);
        }

        public virtual IClientProxy Group(string groupName)
        {
            return new GroupProxy<THub, IClientProxy>(_lifetimeManager, groupName);
        }

        public virtual IClientProxy User(string userId)
        {
            return new UserProxy<THub, IClientProxy>(_lifetimeManager, userId);
        }
    }
}
