// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Microsoft.AspNetCore.SignalR
{
    class TypedHubClients<T> : IHubClients<T>
    {
        private IHubClients _dynamicContext;

        public TypedHubClients(IHubClients dynamicContext)
        {
            _dynamicContext = dynamicContext;
        }

        public T All => TypedClientBuilder<T>.Build(_dynamicContext.All);

        public T Client(string connectionId)
        {
            return TypedClientBuilder<T>.Build(_dynamicContext.Client(connectionId));
        }

        public T Group(string groupName)
        {
            return TypedClientBuilder<T>.Build(_dynamicContext.Group(groupName));
        }

        public T User(string userId)
        {
            return TypedClientBuilder<T>.Build(_dynamicContext.User(userId));
        }
    }
}
