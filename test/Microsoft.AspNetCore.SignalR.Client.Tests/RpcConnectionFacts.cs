// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO.Pipelines;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.Sockets.Client;
using Microsoft.Extensions.Logging;
using Xunit;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.TestHost;

namespace Microsoft.AspNetCore.SignalR.Client.Tests
{
    public class RpcConnectionFacts
    {

        [Fact]
        public async void CheckFixedMessage()
        {

            var loggerFactory = new LoggerFactory();
            var webHostBuilder = new WebHostBuilder().
                ConfigureServices(services =>
                {
                    services.AddSignalR();
                })
                .Configure(app =>
                {
                    app.UseSignalR(routes =>
                    {
                        routes.MapHub<TestHub>("/hubs");
                    });
                });
            var testServer = new TestServer(webHostBuilder);

            using (var httpClient = testServer.CreateClient())
            using (var pipelineFactory = new PipelineFactory())
            {
                var transport = new LongPollingTransport(httpClient, loggerFactory);
                using (var connection = await HubConnection.ConnectAsync(new Uri("http://test/hubs"), new JsonNetInvocationAdapter(), transport, httpClient, pipelineFactory, loggerFactory))
                {

                    var result = await connection.Invoke<string>("Microsoft.AspNetCore.SignalR.Client.Tests.RpcConnectionFacts+TestHub.HelloWorld");
                    Assert.Equal("Hello World!", result);
                }
            }
        }

        [Fact]
        public async void CheckEchoMessage()
        {

            var loggerFactory = new LoggerFactory();
            var webHostBuilder = new WebHostBuilder().
                ConfigureServices(services =>
                {
                    services.AddSignalR();
                })
                .Configure(app =>
                {
                    app.UseSignalR(routes =>
                    {
                        routes.MapHub<TestHub>("/hubs");
                    });
                });
            var testServer = new TestServer(webHostBuilder);

            using (var httpClient = testServer.CreateClient())
            using (var pipelineFactory = new PipelineFactory())
            {
                var transport = new LongPollingTransport(httpClient, loggerFactory);
                using (var connection = await HubConnection.ConnectAsync(new Uri("http://test/hubs"), new JsonNetInvocationAdapter(), transport, httpClient, pipelineFactory, loggerFactory))
                {

                    var result =  await connection.Invoke<string>("Microsoft.AspNetCore.SignalR.Client.Tests.RpcConnectionFacts+TestHub.Echo", "SignalR");
                    Assert.Equal("SignalR", result);
                }
            }
        }

        public class TestHub : Hub
        {
            public string HelloWorld()
            {
                return "Hello World!";
            }
            public string Echo(string message)
            {
                return message;
            }
        }
    }


}
