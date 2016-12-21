// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.IO.Pipelines;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Sockets.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Microsoft.AspNetCore.SignalR.Client.FunctionalTests
{
    public class HubConnectionTests : IDisposable
    {
        private readonly TestServer _testServer;

        public HubConnectionTests()
        {
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
            _testServer = new TestServer(webHostBuilder);
        }

        [Fact]
        public async Task CheckFixedMessage()
        {
            var loggerFactory = new LoggerFactory();

            using (var httpClient = _testServer.CreateClient())
            using (var pipelineFactory = new PipelineFactory())
            {
                var transport = new LongPollingTransport(httpClient, loggerFactory);
                using (var connection = await HubConnection.ConnectAsync(new Uri("http://test/hubs"), new JsonNetInvocationAdapter(), transport, httpClient, pipelineFactory, loggerFactory))
                {
                    var result = await connection.Invoke<string>("HelloWorld");

                    Assert.Equal("Hello World!", result);
                }
            }
        }

        [Fact]
        public async Task CanSendAndReceiveMessage()
        {
            var loggerFactory = new LoggerFactory();
            const string originalMessage = "SignalR";

            using (var httpClient = _testServer.CreateClient())
            using (var pipelineFactory = new PipelineFactory())
            {
                var transport = new LongPollingTransport(httpClient, loggerFactory);
                using (var connection = await HubConnection.ConnectAsync(new Uri("http://test/hubs"), new JsonNetInvocationAdapter(), transport, httpClient, pipelineFactory, loggerFactory))
                {
                    var result = await connection.Invoke<string>("Echo", originalMessage);

                    Assert.Equal(originalMessage, result);
                }
            }
        }

        [Fact]
        public async Task CanInvokeClientMethodFromServer()
        {
            var loggerFactory = new LoggerFactory();
            const string originalMessage = "SignalR";

            using (var httpClient = _testServer.CreateClient())
            using (var pipelineFactory = new PipelineFactory())
            {
                var transport = new LongPollingTransport(httpClient, loggerFactory);
                using (var connection = await HubConnection.ConnectAsync(new Uri("http://test/hubs"), new JsonNetInvocationAdapter(), transport, httpClient, pipelineFactory, loggerFactory))
                {
                    var tcs = new TaskCompletionSource<string>();
                    connection.On("Echo", new[] { typeof(string) }, a =>
                    {
                        tcs.TrySetResult((string)a[0]);
                    });

                    await connection.Invoke<Task>("CallEcho", originalMessage);
                    var completed = await Task.WhenAny(Task.Delay(2000), tcs.Task);
                    Assert.True(completed == tcs.Task, "Receive timed out!");
                    Assert.Equal(originalMessage, tcs.Task.Result);
                }
            }
        }

        [Fact]
        public async Task ServerClosesConnectionIfHubMethodCannotBeResolved()
        {
            var loggerFactory = new LoggerFactory();

            using (var httpClient = _testServer.CreateClient())
            using (var pipelineFactory = new PipelineFactory())
            {
                var transport = new LongPollingTransport(httpClient, loggerFactory);
                using (var connection = await HubConnection.ConnectAsync(new Uri("http://test/hubs"), new JsonNetInvocationAdapter(), transport, httpClient, pipelineFactory, loggerFactory))
                {
                    //TODO: Get rid of this. This is to prevent "No channel" failures due to sends occuring before the first poll.
                    await Task.Delay(500);

                    var ex = await Assert.ThrowsAnyAsync<InvalidOperationException>(
                        async () => await connection.Invoke<Task>("!@#$%"));

                    Assert.Equal(ex.Message, "The hub method '!@#$%' could not be resolved.");
                }
            }
        }

        [Fact]
        public async Task CanSendSendBeforePoll()
        {
            using (var httpClient = _testServer.CreateClient())
            {
                var resp = await httpClient.GetAsync("http://test/hubs/getid");
                var connectionId = await resp.Content.ReadAsStringAsync();

                var invocationAdapter = new JsonNetInvocationAdapter();
                var request = new InvocationDescriptor
                {
                    Method = "Echo",
                    Arguments = new object[] { "Hi" }
                };

                var stream = new MemoryStream();
                await invocationAdapter.WriteMessageAsync(request, stream);
                stream.Position = 0;

                var sendTask = httpClient.PostAsync($"http://test/hubs/send?id={connectionId}",
                    new StreamContent(stream));

                await Task.Delay(100);
                var pollTask = httpClient.PostAsync($"http://test/hubs/poll?id={connectionId}", null);
                await Task.WhenAll(sendTask, pollTask);

                var mockBinder = new Mock<IInvocationBinder>();
                mockBinder.Setup(b => b.GetReturnType(It.IsAny<string>())).Returns(typeof(string));
                var result = (InvocationResultDescriptor)await invocationAdapter.ReadMessageAsync(await pollTask.Result.Content.ReadAsStreamAsync(),
                    mockBinder.Object, CancellationToken.None);

                Assert.Null(result.Error);
                Assert.Equal("Hi", result.Result);
            }
        }

        [Fact]
        public async Task PendingSendCancelledIfPollNotReceivedWithinTimeout()
        {
            using (var httpClient = _testServer.CreateClient())
            {
                var resp = await httpClient.GetAsync("http://test/hubs/getid");
                var connectionId = await resp.Content.ReadAsStringAsync();

                var invocationAdapter = new JsonNetInvocationAdapter();
                var request = new InvocationDescriptor
                {
                    Method = "Echo",
                    Arguments = new object[] { "Hi" }
                };

                var stream = new MemoryStream();
                await invocationAdapter.WriteMessageAsync(request, stream);
                stream.Position = 0;

                var sendTask = httpClient.PostAsync($"http://test/hubs/send?id={connectionId}",
                    new StreamContent(stream));

                var timeoutTask = Task.Delay(10000);

                var completedTask = await Task.WhenAny(sendTask, timeoutTask);
                Assert.Same(sendTask, completedTask);
                Assert.Equal(
                    (await Assert.ThrowsAsync<InvalidOperationException>(async () => await sendTask)).Message,
                    "No channel");
            }
        }

        public void Dispose()
        {
            _testServer.Dispose();
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

            public async Task CallEcho(string message)
            {
                await Clients.Client(Context.ConnectionId).InvokeAsync("Echo", message);
            }
        }
    }
}
