// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Sockets.Internal;
using Microsoft.AspNetCore.Sockets.Transports;
using Xunit;

namespace Microsoft.AspNetCore.Sockets.Tests
{
    public class HttpConnectionDispatcherTests
    {
        [Fact]
        public async Task NegotiateReservesConnectionIdAndReturnsIt()
        {
            var host = SocketsTestHost.CreateWithDefaultEndPoint();
            var result = await host.ExecuteRequestAsync("/negotiate");

            var id = Encoding.UTF8.GetString(result.ResponseBody);

            ConnectionState state;
            Assert.True(host.ConnectionManager.TryGetConnection(id, out state));
            Assert.Equal(id, state.Connection.ConnectionId);
        }

        [Theory]
        [InlineData("/send")]
        [InlineData("/sse")]
        [InlineData("/poll")]
        [InlineData("/ws")]
        public async Task EndpointsThatAcceptConnectionId404WhenUnknownConnectionIdProvided(string path)
        {
            var host = SocketsTestHost.CreateWithDefaultEndPoint();
            var result = await host.ExecuteRequestAsync(path, queryString: "?id=unknown");
            Assert.Equal(StatusCodes.Status404NotFound, result.HttpContext.Response.StatusCode);
            Assert.Equal("No Connection with that ID", Encoding.UTF8.GetString(result.ResponseBody));
        }

        [Theory]
        [InlineData("/send")]
        [InlineData("/sse")]
        [InlineData("/poll")]
        public async Task EndpointsThatRequireConnectionId400WhenNoConnectionIdProvided(string path)
        {
            var host = SocketsTestHost.CreateWithDefaultEndPoint();
            var result = await host.ExecuteRequestAsync(path);
            Assert.Equal(StatusCodes.Status400BadRequest, result.HttpContext.Response.StatusCode);
            Assert.Equal("Connection ID required", Encoding.UTF8.GetString(result.ResponseBody));
        }

        [Fact]
        public async Task CannotUseSendEndpointWithWebSockets()
        {
            var host = SocketsTestHost.CreateWithDefaultEndPoint();
            var connectionState = host.CreateConnection(transportName: WebSocketsTransport.TransportName);
            var result = await host.ExecuteRequestAsync("/send", queryString: $"?id={connectionState.Connection.ConnectionId}");
            Assert.Equal(StatusCodes.Status400BadRequest, result.HttpContext.Response.StatusCode);
            Assert.Equal("Cannot send to a WebSockets connection", Encoding.UTF8.GetString(result.ResponseBody));
        }
    }
}
