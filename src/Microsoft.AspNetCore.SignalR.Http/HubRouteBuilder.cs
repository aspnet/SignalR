// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Sockets;

namespace Microsoft.AspNetCore.SignalR
{
    public class HubRouteBuilder
    {
        private readonly SocketRouteBuilder _routes;

        public HubRouteBuilder(SocketRouteBuilder routes)
        {
            _routes = routes;
        }

        public void MapHub<THub>(string path) where THub : IHub
        {
            MapHub<THub>(path, socketOptions: null);
        }

        public void MapHub<THub>(string path, Action<HttpSocketOptions> socketOptions) where THub : IHub
        {
            var hubType = typeof(THub);

            while (hubType != null)
            {
                if (hubType.IsGenericType && hubType.GetGenericTypeDefinition() == typeof(Hub<>))
                {
                    var clientType = hubType.GetGenericArguments()[0];
                    var method = typeof(HubRouteBuilder).GetMethod(nameof(MapHubCore), BindingFlags.NonPublic | BindingFlags.Instance)
                                                        .MakeGenericMethod(typeof(THub), clientType);
                    method.Invoke(this, new object[] { path, socketOptions });

                    return;
                }

                hubType = hubType.BaseType;
            }

            // Error, not a Hub<T>
        }

        private void MapHubCore<THub, TClient>(string path, Action<HttpSocketOptions> socketOptions) where THub : Hub<TClient>
        {
            // find auth attributes
            var authorizeAttribute = typeof(THub).GetCustomAttribute<AuthorizeAttribute>(inherit: true);
            var options = new HttpSocketOptions();
            if (authorizeAttribute != null)
            {
                options.AuthorizationData.Add(authorizeAttribute);
            }
            socketOptions?.Invoke(options);

            _routes.MapSocket(path, options, builder =>
            {
                builder.UseHub<THub, TClient>();
            });
        }
    }
}
