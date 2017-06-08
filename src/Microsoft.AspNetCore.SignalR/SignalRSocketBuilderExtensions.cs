using System;
using System.Collections.Generic;
using System.Text;
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
