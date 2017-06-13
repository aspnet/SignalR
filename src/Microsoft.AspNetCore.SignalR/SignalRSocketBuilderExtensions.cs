// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Sockets;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.AspNetCore.SignalR
{
    public static class SignalRSocketBuilderExtensions
    {
        public static ISocketBuilder UseHub<THub, TClient>(this ISocketBuilder socketBuilder) where THub : Hub<TClient>
        {
            var endpoint = socketBuilder.ApplicationServices.GetRequiredService<HubEndPoint<THub, TClient>>();
            return socketBuilder.Run(connection =>
            {
                return endpoint.OnConnectedAsync(connection);
            });
        }
    }
}
