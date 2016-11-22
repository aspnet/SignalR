// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Sockets;
using Microsoft.Extensions.Internal;

namespace Microsoft.AspNetCore.SignalR
{
    public class HubConnection : IClientProxy
    {
        private object _lock = new object();
        private Task _taskQueue = TaskCache.CompletedTask;
        private Connection _connection;
        private InvocationAdapterRegistry _registry;

        internal Stream Stream;

        public ClaimsPrincipal User => _connection.User;
        public string ConnectionId => _connection.ConnectionId;
        public ConnectionMetadata Metadata => _connection.Metadata;

        public HubConnection(Connection connection, InvocationAdapterRegistry registry)
        {
            _connection = connection;
            _registry = registry;
            Stream = _connection.Channel.GetStream();
        }

        public Task InvokeAsync(string method, params object[] args)
        {
            var invocationAdapter = _registry.GetInvocationAdapter(_connection.Metadata.Get<string>("formatType"));

            var message = new InvocationDescriptor
            {
                Method = method,
                Arguments = args
            };

            return Enqueue(() => invocationAdapter.WriteInvocationDescriptorAsync(message, Stream));
        }

        internal Task Enqueue(Func<Task> taskFactory)
        {
            lock (_lock)
            {
                return _taskQueue = _taskQueue.ContinueWith((t) => taskFactory());
            }
        }
    }
}
