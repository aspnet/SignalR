// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Microsoft.AspNetCore.SignalR.Client
{
    public class HubConnectionBuilder : IHubConnectionBuilder
    {
        public IServiceCollection Services { get; } = new ServiceCollection();

        public HubConnection Build()
        {
            var provider = Services.BuildServiceProvider();

            var connectionFactory = provider.GetService<Func<IConnection>>();
            if (connectionFactory == null)
            {
                throw new InvalidOperationException("Cannot create HubConnection instance. A connection was not configured.");
            }

            var hubProtocol = provider.GetService<IHubProtocol>();
            var loggerFactory = provider.GetService<ILoggerFactory>();

            return new HubConnection(connectionFactory, hubProtocol ?? new JsonHubProtocol(), loggerFactory);
        }

        // Prevents from being displayed in intellisense
        [EditorBrowsable(EditorBrowsableState.Never)]
        public override int GetHashCode()
        {
            return base.GetHashCode();
        }

        // Prevents from being displayed in intellisense
        [EditorBrowsable(EditorBrowsableState.Never)]
        public override bool Equals(object obj)
        {
            return base.Equals(obj);
        }

        // Prevents from being displayed in intellisense
        [EditorBrowsable(EditorBrowsableState.Never)]
        public override string ToString()
        {
            return base.ToString();
        }

        // Prevents from being displayed in intellisense
        [EditorBrowsable(EditorBrowsableState.Never)]
        public new Type GetType()
        {
            return base.GetType();
        }
    }
}
