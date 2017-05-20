using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.AspNetCore.Sockets
{
    public static class HttpSocketBuilderExtensions
    {
        public static ISocketBuilder UseEndPoint<TEndPoint>(this ISocketBuilder socketBuilder) where TEndPoint : EndPoint
        {
            // This is a terminal middleware, so there's no need to use the 'next' parameter
            return socketBuilder.Use((connection, _) =>
            {
                var endpoint = socketBuilder.ApplicationServices.GetRequiredService<TEndPoint>();
                return endpoint.OnConnectedAsync(connection);
            });
        }
    }
}
