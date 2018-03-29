// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Microsoft.AspNetCore.SignalR.Client.Tests
{
    // This includes tests that verify HubConnection conforms to the Hub Protocol, without setting up a full server (even TestServer).
    // We can also have more control over the messages we send to HubConnection in order to ensure that protocol errors and other quirks
    // don't cause problems.
    public partial class HubConnectionTests
    {
        public class Protocol
        {
            [Fact]
            public async Task SendAsyncSendsANonBlockingInvocationMessage()
            {
                var connection = new TestConnection();
                var hubConnection = CreateHubConnection(connection);
                try
                {
                    await hubConnection.StartAsync().OrTimeout();

                    var invokeTask = hubConnection.SendAsync("Foo").OrTimeout();

                    var invokeMessage = await connection.ReadSentTextMessageAsync().OrTimeout();

                    // ReadSentTextMessageAsync strips off the record separator (because it has use it as a separator now that we use Pipelines)
                    Assert.Equal("{\"type\":1,\"target\":\"Foo\",\"arguments\":[]}", invokeMessage);
                }
                finally
                {
                    await hubConnection.DisposeAsync().OrTimeout();
                    await connection.DisposeAsync().OrTimeout();
                }
            }

            [Fact]
            public async Task ClientSendsHandshakeMessageWhenStartingConnection()
            {
                var connection = new TestConnection(autoHandshake: false);
                var hubConnection = CreateHubConnection(connection);
                try
                {
                    // We can't await StartAsync because it depends on the negotiate process!
                    var startTask = hubConnection.StartAsync().OrTimeout();

                    var handshakeMessage = await connection.ReadHandshakeAndSendResponseAsync().OrTimeout();

                    // ReadSentTextMessageAsync strips off the record separator (because it has use it as a separator now that we use Pipelines)
                    Assert.Equal("{\"protocol\":\"json\",\"version\":1}", handshakeMessage);

                    await startTask;
                }
                finally
                {
                    await hubConnection.DisposeAsync().OrTimeout();
                    await connection.DisposeAsync().OrTimeout();
                }
            }

            [Fact]
            public async Task InvokeSendsAnInvocationMessage()
            {
                var connection = new TestConnection();
                var hubConnection = CreateHubConnection(connection);
                try
                {
                    await hubConnection.StartAsync().OrTimeout();

                    var invokeTask = hubConnection.InvokeAsync("Foo").OrTimeout();

                    var invokeMessage = await connection.ReadSentTextMessageAsync().OrTimeout();

                    // ReadSentTextMessageAsync strips off the record separator (because it has use it as a separator now that we use Pipelines)
                    Assert.Equal("{\"type\":1,\"invocationId\":\"1\",\"target\":\"Foo\",\"arguments\":[]}", invokeMessage);
                }
                finally
                {
                    await hubConnection.DisposeAsync().OrTimeout();
                    await connection.DisposeAsync().OrTimeout();
                }
            }

            [Fact]
            public async Task ReceiveCloseMessageWithoutErrorWillCloseHubConnection()
            {
                TaskCompletionSource<Exception> closedTcs = new TaskCompletionSource<Exception>();

                var connection = new TestConnection();
                var hubConnection = CreateHubConnection(connection);
                hubConnection.Closed += e => closedTcs.SetResult(e);

                try
                {
                    await hubConnection.StartAsync().OrTimeout();

                    await connection.ReceiveJsonMessage(new { type = 7 }).OrTimeout();

                    Exception closeException = await closedTcs.Task.OrTimeout();
                    Assert.Null(closeException);
                }
                finally
                {
                    await hubConnection.DisposeAsync().OrTimeout();
                    await connection.DisposeAsync().OrTimeout();
                }
            }

            [Fact]
            public async Task ReceiveCloseMessageWithErrorWillCloseHubConnection()
            {
                TaskCompletionSource<Exception> closedTcs = new TaskCompletionSource<Exception>();

                var connection = new TestConnection();
                var hubConnection = CreateHubConnection(connection);
                hubConnection.Closed += e => closedTcs.SetResult(e);

                try
                {
                    await hubConnection.StartAsync().OrTimeout();

                    await connection.ReceiveJsonMessage(new { type = 7, error = "Error!" }).OrTimeout();

                    Exception closeException = await closedTcs.Task.OrTimeout();
                    Assert.NotNull(closeException);
                    Assert.Equal("The server closed the connection with the following error: Error!", closeException.Message);
                }
                finally
                {
                    await hubConnection.DisposeAsync().OrTimeout();
                }
            }

            [Fact]
            public async Task StreamSendsAnInvocationMessage()
            {
                var connection = new TestConnection();
                var hubConnection = CreateHubConnection(connection);
                try
                {
                    await hubConnection.StartAsync().OrTimeout();

                    var channel = await hubConnection.StreamAsChannelAsync<object>("Foo").OrTimeout();

                    var invokeMessage = await connection.ReadSentTextMessageAsync().OrTimeout();

                    // ReadSentTextMessageAsync strips off the record separator (because it has use it as a separator now that we use Pipelines)
                    Assert.Equal("{\"type\":4,\"invocationId\":\"1\",\"target\":\"Foo\",\"arguments\":[]}", invokeMessage);

                    // Complete the channel
                    await connection.ReceiveJsonMessage(new { invocationId = "1", type = 3 }).OrTimeout();
                    await channel.Completion;
                }
                finally
                {
                    await hubConnection.DisposeAsync().OrTimeout();
                    await connection.DisposeAsync().OrTimeout();
                }
            }

            [Fact]
            public async Task InvokeCompletedWhenCompletionMessageReceived()
            {
                var connection = new TestConnection();
                var hubConnection = CreateHubConnection(connection);
                try
                {
                    await hubConnection.StartAsync().OrTimeout();

                    var invokeTask = hubConnection.InvokeAsync("Foo").OrTimeout();

                    await connection.ReceiveJsonMessage(new { invocationId = "1", type = 3 }).OrTimeout();

                    await invokeTask.OrTimeout();
                }
                finally
                {
                    await hubConnection.DisposeAsync().OrTimeout();
                    await connection.DisposeAsync().OrTimeout();
                }
            }

            [Fact]
            public async Task StreamCompletesWhenCompletionMessageIsReceived()
            {
                var connection = new TestConnection();
                var hubConnection = CreateHubConnection(connection);
                try
                {
                    await hubConnection.StartAsync().OrTimeout();

                    var channel = await hubConnection.StreamAsChannelAsync<int>("Foo").OrTimeout();

                    await connection.ReceiveJsonMessage(new { invocationId = "1", type = 3 }).OrTimeout();

                    Assert.Empty(await channel.ReadAllAsync());
                }
                finally
                {
                    await hubConnection.DisposeAsync().OrTimeout();
                    await connection.DisposeAsync().OrTimeout();
                }
            }

            [Fact]
            public async Task InvokeYieldsResultWhenCompletionMessageReceived()
            {
                var connection = new TestConnection();
                var hubConnection = CreateHubConnection(connection);
                try
                {
                    await hubConnection.StartAsync().OrTimeout();

                    var invokeTask = hubConnection.InvokeAsync<int>("Foo").OrTimeout();

                    await connection.ReceiveJsonMessage(new { invocationId = "1", type = 3, result = 42 }).OrTimeout();

                    Assert.Equal(42, await invokeTask.OrTimeout());
                }
                finally
                {
                    await hubConnection.DisposeAsync().OrTimeout();
                    await connection.DisposeAsync().OrTimeout();
                }
            }

            [Fact]
            public async Task InvokeFailsWithExceptionWhenCompletionWithErrorReceived()
            {
                var connection = new TestConnection();
                var hubConnection = CreateHubConnection(connection);
                try
                {
                    await hubConnection.StartAsync().OrTimeout();

                    var invokeTask = hubConnection.InvokeAsync<int>("Foo").OrTimeout();

                    await connection.ReceiveJsonMessage(new { invocationId = "1", type = 3, error = "An error occurred" }).OrTimeout();

                    var ex = await Assert.ThrowsAsync<HubException>(() => invokeTask).OrTimeout();
                    Assert.Equal("An error occurred", ex.Message);
                }
                finally
                {
                    await hubConnection.DisposeAsync().OrTimeout();
                    await connection.DisposeAsync().OrTimeout();
                }
            }

            [Fact]
            public async Task StreamFailsIfCompletionMessageHasPayload()
            {
                var connection = new TestConnection();
                var hubConnection = CreateHubConnection(connection);
                try
                {
                    await hubConnection.StartAsync().OrTimeout();

                    var channel = await hubConnection.StreamAsChannelAsync<string>("Foo").OrTimeout();

                    await connection.ReceiveJsonMessage(new { invocationId = "1", type = 3, result = "Oops" }).OrTimeout();

                    var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await channel.ReadAllAsync().OrTimeout());
                    Assert.Equal("Server provided a result in a completion response to a streamed invocation.", ex.Message);
                }
                finally
                {
                    await hubConnection.DisposeAsync().OrTimeout();
                    await connection.DisposeAsync().OrTimeout();
                }
            }

            [Fact]
            public async Task StreamFailsWithExceptionWhenCompletionWithErrorReceived()
            {
                var connection = new TestConnection();
                var hubConnection = CreateHubConnection(connection);
                try
                {
                    await hubConnection.StartAsync().OrTimeout();

                    var channel = await hubConnection.StreamAsChannelAsync<int>("Foo").OrTimeout();

                    await connection.ReceiveJsonMessage(new { invocationId = "1", type = 3, error = "An error occurred" }).OrTimeout();

                    var ex = await Assert.ThrowsAsync<HubException>(async () => await channel.ReadAllAsync().OrTimeout());
                    Assert.Equal("An error occurred", ex.Message);
                }
                finally
                {
                    await hubConnection.DisposeAsync().OrTimeout();
                    await connection.DisposeAsync().OrTimeout();
                }
            }

            [Fact]
            public async Task InvokeFailsWithErrorWhenStreamingItemReceived()
            {
                var connection = new TestConnection();
                var hubConnection = CreateHubConnection(connection);
                try
                {
                    await hubConnection.StartAsync().OrTimeout();

                    var invokeTask = hubConnection.InvokeAsync<int>("Foo").OrTimeout();

                    await connection.ReceiveJsonMessage(new { invocationId = "1", type = 2, item = 42 }).OrTimeout();

                    var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => invokeTask).OrTimeout();
                    Assert.Equal("Streaming hub methods must be invoked with the 'HubConnection.StreamAsChannelAsync' method.", ex.Message);
                }
                finally
                {
                    await hubConnection.DisposeAsync().OrTimeout();
                    await connection.DisposeAsync().OrTimeout();
                }
            }

            [Fact]
            public async Task StreamYieldsItemsAsTheyArrive()
            {
                var connection = new TestConnection();
                var hubConnection = CreateHubConnection(connection);
                try
                {
                    await hubConnection.StartAsync().OrTimeout();

                    var channel = await hubConnection.StreamAsChannelAsync<string>("Foo").OrTimeout();

                    await connection.ReceiveJsonMessage(new { invocationId = "1", type = 2, item = "1" }).OrTimeout();
                    await connection.ReceiveJsonMessage(new { invocationId = "1", type = 2, item = "2" }).OrTimeout();
                    await connection.ReceiveJsonMessage(new { invocationId = "1", type = 2, item = "3" }).OrTimeout();
                    await connection.ReceiveJsonMessage(new { invocationId = "1", type = 3 }).OrTimeout();

                    var notifications = await channel.ReadAllAsync().OrTimeout();

                    Assert.Equal(new[] { "1", "2", "3", }, notifications.ToArray());
                }
                finally
                {
                    await hubConnection.DisposeAsync().OrTimeout();
                    await connection.DisposeAsync().OrTimeout();
                }
            }

            [Fact]
            public async Task HandlerRegisteredWithOnIsFiredWhenInvocationReceived()
            {
                var connection = new TestConnection();
                var hubConnection = CreateHubConnection(connection);
                var handlerCalled = new TaskCompletionSource<object[]>();
                try
                {
                    await hubConnection.StartAsync().OrTimeout();

                    hubConnection.On<int, string, float>("Foo", (r1, r2, r3) => handlerCalled.TrySetResult(new object[] { r1, r2, r3 }));

                    var args = new object[] { 1, "Foo", 2.0f };
                    await connection.ReceiveJsonMessage(new { invocationId = "1", type = 1, target = "Foo", arguments = args }).OrTimeout();

                    Assert.Equal(args, await handlerCalled.Task.OrTimeout());
                }
                finally
                {
                    await hubConnection.DisposeAsync().OrTimeout();
                    await connection.DisposeAsync().OrTimeout();
                }
            }

            [Fact]
            public async Task AcceptsPingMessages()
            {
                var connection = new TestConnection();
                var hubConnection = CreateHubConnection(connection);

                try
                {
                    await hubConnection.StartAsync().OrTimeout();

                    // Send an invocation
                    var invokeTask = hubConnection.InvokeAsync("Foo").OrTimeout();

                    // Receive the ping mid-invocation so we can see that the rest of the flow works fine
                    await connection.ReceiveJsonMessage(new { type = 6 }).OrTimeout();

                    // Receive a completion
                    await connection.ReceiveJsonMessage(new { invocationId = "1", type = 3 }).OrTimeout();

                    // Ensure the invokeTask completes properly
                    await invokeTask.OrTimeout();
                }
                finally
                {
                    await hubConnection.DisposeAsync().OrTimeout();
                    await connection.DisposeAsync().OrTimeout();
                }
            }

            [Fact]
            public async Task PartialHandshakeResponseWorks()
            {
                var connection = new TestConnection(synchronousCallbacks: true, autoHandshake: false);
                var hubConnection = CreateHubConnection(connection);
                try
                {
                    var task = hubConnection.StartAsync();

                    await connection.ReceiveTextAsync("{");

                    Assert.False(task.IsCompleted);

                    await connection.ReceiveTextAsync("}");

                    Assert.False(task.IsCompleted);

                    await connection.ReceiveTextAsync("\u001e");

                    await task.OrTimeout();
                }
                finally
                {
                    await hubConnection.DisposeAsync().OrTimeout();
                    await connection.DisposeAsync().OrTimeout();
                }
            }

            [Fact]
            public async Task HandshakeAndInvocationInSameBufferWorks()
            {
                var payload = "{}\u001e{\"type\":1, \"target\": \"Echo\", \"arguments\":[\"hello\"]}\u001e";
                var connection = new TestConnection(synchronousCallbacks: true, autoHandshake: false);
                var hubConnection = CreateHubConnection(connection);
                try
                {
                    var tcs = new TaskCompletionSource<string>();
                    hubConnection.On<string>("Echo", data =>
                    {
                        tcs.TrySetResult(data);
                    });

                    await connection.ReceiveTextAsync(payload);

                    await hubConnection.StartAsync();

                    var response = await tcs.Task.OrTimeout();
                    Assert.Equal("hello", response);
                }
                finally
                {
                    await hubConnection.DisposeAsync().OrTimeout();
                    await connection.DisposeAsync().OrTimeout();
                }
            }

            [Fact]
            public async Task PartialInvocationWorks()
            {                
                var connection = new TestConnection(synchronousCallbacks: true);
                var hubConnection = CreateHubConnection(connection);
                try
                {
                    var tcs = new TaskCompletionSource<string>();
                    hubConnection.On<string>("Echo", data =>
                    {
                        tcs.TrySetResult(data);
                    });

                    await hubConnection.StartAsync().OrTimeout();

                    await connection.ReceiveTextAsync("{\"type\":1, ");

                    Assert.False(tcs.Task.IsCompleted);

                    await connection.ReceiveTextAsync("\"target\": \"Echo\", \"arguments\"");

                    Assert.False(tcs.Task.IsCompleted);

                    await connection.ReceiveTextAsync(":[\"hello\"]}\u001e");

                    Assert.True(tcs.Task.IsCompleted);

                    var response = await tcs.Task;

                    Assert.Equal("hello", response);
                }
                finally
                {
                    await hubConnection.DisposeAsync().OrTimeout();
                    await connection.DisposeAsync().OrTimeout();
                }
            }
        }
    }
}
