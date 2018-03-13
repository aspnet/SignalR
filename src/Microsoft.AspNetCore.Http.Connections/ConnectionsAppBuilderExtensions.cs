// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.AspNetCore.Builder
{
    public static class ConnectionsAppBuilderExtensions
    {
        // 14 is the maximum websocket frame header size
        // See https://github.com/dotnet/corefx/blob/1df4a4866a90f22f861156b8ff496c24103d39cc/src/Common/src/System/Net/WebSockets/ManagedWebSocket.cs#L61
        // And where it is used https://github.com/dotnet/corefx/blob/1df4a4866a90f22f861156b8ff496c24103d39cc/src/Common/src/System/Net/WebSockets/ManagedWebSocket.cs#L188
        private const int ReceiveBufferSize = 14;

        public static IApplicationBuilder UseConnections(this IApplicationBuilder app, Action<SocketRouteBuilder> callback)
        {
            var dispatcher = app.ApplicationServices.GetRequiredService<HttpConnectionDispatcher>();

            var routes = new RouteBuilder(app);

            callback(new ConnectionsRouteBuilder(routes, dispatcher));

            app.UseWebSockets(new WebSocketOptions()
            {
                ReceiveBufferSize = ReceiveBufferSize
            });
            app.UseRouter(routes.Build());
            return app;
        }
    }
}
