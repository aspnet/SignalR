// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Routing;

namespace Microsoft.AspNetCore.Sockets
{
    public class ConnectionsRouteBuilder
    {
        private readonly HttpConnectionDispatcher _dispatcher;
        private readonly RouteBuilder _routes;

        public ConnectionsRouteBuilder(RouteBuilder routes, HttpConnectionDispatcher dispatcher)
        {
            _routes = routes;
            _dispatcher = dispatcher;
        }

        public void MapConnections(string path, Action<IConnectionBuilder> socketConfig) =>
            MapConnections(new PathString(path), new HttpSocketOptions(), socketConfig);

        public void MapConnections(PathString path, Action<IConnectionBuilder> socketConfig) =>
            MapConnections(path, new HttpSocketOptions(), socketConfig);

        public void MapConnections(PathString path, HttpSocketOptions options, Action<IConnectionBuilder> connectionConfig)
        {
            var connectionBuilder = new ConnectionBuilder(_routes.ServiceProvider);
            connectionConfig(connectionBuilder);
            var socket = connectionBuilder.Build();
            _routes.MapRoute(path, c => _dispatcher.ExecuteAsync(c, options, socket));
            _routes.MapRoute(path + "/negotiate", c => _dispatcher.ExecuteNegotiateAsync(c, options));
        }

        public void MapConnectionHandler<TConnectionHandler>(string path) where TConnectionHandler : ConnectionHandler
        {
            MapConnectionHandler<TConnectionHandler>(new PathString(path), socketOptions: null);
        }

        public void MapConnectionHandler<TConnectionHandler>(PathString path) where TConnectionHandler : ConnectionHandler
        {
            MapConnectionHandler<TConnectionHandler>(path, socketOptions: null);
        }

        public void MapConnectionHandler<TConnectionHandler>(PathString path, Action<HttpSocketOptions> socketOptions) where TConnectionHandler : ConnectionHandler
        {
            var authorizeAttributes = typeof(TConnectionHandler).GetCustomAttributes<AuthorizeAttribute>(inherit: true);
            var options = new HttpSocketOptions();
            foreach (var attribute in authorizeAttributes)
            {
                options.AuthorizationData.Add(attribute);
            }
            socketOptions?.Invoke(options);

            MapConnections(path, options, builder =>
            {
                builder.UseConnectionHandler<TConnectionHandler>();
            });
        }
    }
}
