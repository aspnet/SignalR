// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.AspNetCore.SignalR.Tests
{
    [CollectionDefinition(Name)]
    public class EndToEndTestsCollection : ICollectionFixture<ServerFixture>
    {
        public const string Name = "EndToEndTests";
    }

    [Collection(EndToEndTestsCollection.Name)]
    public class EndToEndTests
    {
        private readonly ServerFixture _serverFixture;

        public EndToEndTests(ServerFixture serverFixture)
        {
            _serverFixture = serverFixture;
        }

        [Fact]
        public async Task WebSocketsTest()
        {
            const string message = "Hello, World!";
            using (var ws = new ClientWebSocket())
            {
                await ws.ConnectAsync(new Uri(_serverFixture.WebSocketsUrl + "/echo/ws"), CancellationToken.None);
                var bytes = System.Text.Encoding.UTF8.GetBytes(message);
                await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Binary, true, CancellationToken.None);

                var buffer = new ArraySegment<byte>(new byte[1024]);
                var result = await ws.ReceiveAsync(buffer, CancellationToken.None);

                Assert.Equal(message, System.Text.Encoding.UTF8.GetString(buffer.Array, 0, result.Count));

                await ws.CloseAsync(WebSocketCloseStatus.Empty, "", CancellationToken.None);
            }
        }
    }
}
