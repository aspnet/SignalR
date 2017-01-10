// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.AspNetCore.SignalR.Tests
{
    [CollectionDefinition(Name)]
    public class FunctionalTestsCollection : ICollectionFixture<ServerFixture>
    {
        public const string Name = "FunctionalTests";
    }

    [Collection(FunctionalTestsCollection.Name)]
    public class WebSocketsTests
    {
        private readonly ServerFixture _serverFixture;

        public WebSocketsTests(ServerFixture serverFixture)
        {
            _serverFixture = serverFixture;
        }

        [Fact]
        public async Task CheckEchoWebSockets()
        {
            const string message = "Hello, World!";

            using (var ws = new ClientWebSocket())
            {
                var url = _serverFixture.BaseUrl.Replace("http", "ws") + "echo/ws";

                await ws.ConnectAsync(new Uri(url), CancellationToken.None);
                var bytes = Encoding.UTF8.GetBytes(message);
                await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Binary, true, CancellationToken.None);

                var buffer = new ArraySegment<byte>(new byte[1024]);
                var result = await ws.ReceiveAsync(buffer, CancellationToken.None);

                Assert.Equal(message, Encoding.UTF8.GetString(buffer.Array, 0, result.Count));

                await ws.CloseAsync(WebSocketCloseStatus.Empty, "", CancellationToken.None);
            }
        }
    }
}
