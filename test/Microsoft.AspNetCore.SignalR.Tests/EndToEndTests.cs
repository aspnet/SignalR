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

    public class EndToEndTests
    {
        public class TestClass
        {
            [Fact]
            public async Task WebSocketsTest()
            {
                var host = new WebHostBuilder()
                .UseKestrel()
                .UseContentRoot(Directory.GetCurrentDirectory())
                .UseStartup<Startup>()
                .Build();

                var lifetime = host.Services.GetRequiredService<IApplicationLifetime>();
                var cts = new CancellationTokenSource();

                var thread = new Thread(() => host.Run(cts.Token));
                thread.Start();

                const string message = "Hello, World!";

                using (var ws = new ClientWebSocket())
                {
                    await ws.ConnectAsync(new Uri("ws://localhost:5000/echo/ws"), CancellationToken.None);
                    var bytes = System.Text.Encoding.UTF8.GetBytes(message);
                    await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Binary, true, CancellationToken.None);

                    var buffer = new ArraySegment<byte>(new byte[1024]);
                    var result = await ws.ReceiveAsync(buffer, CancellationToken.None);

                    Assert.Equal(message, System.Text.Encoding.UTF8.GetString(buffer.Array, 0, result.Count));

                    await ws.CloseAsync(WebSocketCloseStatus.Empty, "", CancellationToken.None);

                    cts.Cancel();
                    lifetime.ApplicationStopping.WaitHandle.WaitOne();
                }
            }
        }

    }

}
