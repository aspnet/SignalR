// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.IO.Pipelines;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Microsoft.AspNetCore.SignalR.Tests
{
    public class EchoEndPoint : EndPoint
    {
        public async override Task OnConnectedAsync(Connection connection)
        {
            await connection.Channel.Input.CopyToAsync(connection.Channel.Output);
        }
    }

    public class Startup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddSockets();
            services.AddSignalR();
            services.AddSingleton<EchoEndPoint>();
        }

        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            app.UseSockets(options => options.MapEndpoint<EchoEndPoint>("/echo"));
            app.UseSignalR(routes =>
            {
            });
        }
    }


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
                    await ws.ConnectAsync(new Uri("ws://localhost:3000/echo/ws"), CancellationToken.None);
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
