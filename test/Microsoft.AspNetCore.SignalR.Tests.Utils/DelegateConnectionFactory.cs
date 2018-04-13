// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace Microsoft.AspNetCore.SignalR.Tests
{
    public class DelegateConnectionFactory : IConnectionFactory
    {
        private readonly Func<TransferFormat, Task<ConnectionContext>> _connectionFactory;

        public DelegateConnectionFactory(Func<TransferFormat, Task<ConnectionContext>> connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public Task<ConnectionContext> ConnectAsync(TransferFormat transferFormat)
        {
            return _connectionFactory(transferFormat);
        }
    }
}
