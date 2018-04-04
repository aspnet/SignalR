// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.ComponentModel;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.SignalR.Client
{
    public class HubConnectionBuilder : IHubConnectionBuilder
    {
        private ServiceProvider _serviceProvider;

        public IServiceCollection Services { get; }

        public HubConnectionBuilder()
        {
            Services = new ServiceCollection();
            Services.AddSingleton<HubConnection>(serviceProvider =>
            {
                var connectionFactory = serviceProvider.GetService<Func<IConnection>>();
                if (connectionFactory == null)
                {
                    throw new InvalidOperationException("Cannot create HubConnection instance. A connection was not configured.");
                }

                var hubProtocol = serviceProvider.GetService<IHubProtocol>();
                var loggerFactory = serviceProvider.GetService<ILoggerFactory>();

                // Note that the root service provider that can be disposed is passed to HubConnection
                return new HubConnection(connectionFactory, hubProtocol ?? new JsonHubProtocol(), _serviceProvider, loggerFactory);
            });
        }

        public HubConnection Build()
        {
            // Build can only be used once
            if (_serviceProvider != null)
            {
                throw new InvalidOperationException("HubConnectionBuilder allows creation only of a single instance of HubConnection.");
            }

            // The service provider is disposed by the HubConnection
            _serviceProvider = Services.BuildServiceProvider();

            return _serviceProvider.GetService<HubConnection>();
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
