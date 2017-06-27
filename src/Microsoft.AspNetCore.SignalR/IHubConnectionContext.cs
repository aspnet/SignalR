// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Microsoft.AspNetCore.SignalR
{
    public interface IHubConnectionContext
    {
        IHubProxy All { get; }

        IHubClientProxy Client(string connectionId);

        IHubProxy Group(string groupName);

        IHubProxy User(string userId);
    }
}
