// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Microsoft.AspNetCore.SignalR
{
    public interface IHubClients
    {
        IClientProxy All { get; }

        IClientProxy Client(string connectionId);

        IClientProxy Group(string groupName);

        IClientProxy User(string userId);
    }
}
