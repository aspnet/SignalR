// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading.Tasks;

namespace Microsoft.AspNetCore.SignalR
{
    public interface IGroupManager
    {
        Task AddAsync(string groupName, string connectionId);
        Task RemoveAsync(string groupName, string connectionId);
    }
}
