// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Microsoft.AspNetCore.SignalR.Tests.Common;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Xunit;
using System.Reactive;
using System.Linq;
using System.Reactive.Threading.Tasks;

namespace Microsoft.AspNetCore.SignalR.Client.Tests
{
    // This includes tests that verify HubConnection conforms to the Hub Protocol, without setting up a full server (even TestServer).
    // We can also have more control over the messages we send to HubConnection in order to ensure that protocol errors and other quirks
    // don't cause problems.
    public class HubConnectionProtocolTests
    {
        [Fact]
        public async Task InvokeSendsAnInvocationMessage()
        {
            var connection = new TestConnection();
            var hubConnection = new HubConnection(connection, new JsonHubProtocol(new JsonSerializer()), new LoggerFactory());
            try
            {
                await hubConnection.StartAsync();

                var invokeTask = hubConnection.Invoke("Foo");

                var invokeMessage = await connection.ReadSentTextMessageAsync().OrTimeout();

                Assert.Equal("{\"invocationId\":\"1\",\"type\":1,\"target\":\"Foo\",\"arguments\":[]}", invokeMessage);
            }
            finally
            {
                await hubConnection.DisposeAsync().OrTimeout();
                await connection.DisposeAsync().OrTimeout();
            }
        }

        [Fact]
        public async Task StreamSendsAnInvocationMessage()
        {
            var connection = new TestConnection();
            var hubConnection = new HubConnection(connection, new JsonHubProtocol(new JsonSerializer()), new LoggerFactory());
            try
            {
                await hubConnection.StartAsync();

                var observable = hubConnection.Stream<object>("Foo");

                var invokeMessage = await connection.ReadSentTextMessageAsync().OrTimeout();

                Assert.Equal("{\"invocationId\":\"1\",\"type\":1,\"target\":\"Foo\",\"arguments\":[]}", invokeMessage);
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
            var hubConnection = new HubConnection(connection, new JsonHubProtocol(new JsonSerializer()), new LoggerFactory());
            try
            {
                await hubConnection.StartAsync();

                var invokeTask = hubConnection.Invoke("Foo");

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
            var hubConnection = new HubConnection(connection, new JsonHubProtocol(new JsonSerializer()), new LoggerFactory());
            try
            {
                await hubConnection.StartAsync();

                var observable = hubConnection.Stream<int>("Foo");

                await connection.ReceiveJsonMessage(new { invocationId = "1", type = 3 }).OrTimeout();

                Assert.True(await observable.IsEmpty().Timeout(TimeSpan.FromSeconds(1)));
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
            var hubConnection = new HubConnection(connection, new JsonHubProtocol(new JsonSerializer()), new LoggerFactory());
            try
            {
                await hubConnection.StartAsync();

                var invokeTask = hubConnection.Invoke<int>("Foo");

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
        public async Task StreamFailsIfCompletionMessageHasPayload()
        {
            var connection = new TestConnection();
            var hubConnection = new HubConnection(connection, new JsonHubProtocol(new JsonSerializer()), new LoggerFactory());
            try
            {
                await hubConnection.StartAsync();

                var observable = hubConnection.Stream<string>("Foo");

                await connection.ReceiveJsonMessage(new { invocationId = "1", type = 3, result = "Oops" }).OrTimeout();

                var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await observable.Timeout(TimeSpan.FromSeconds(1)));
                Assert.Equal("Server provided a result in a completion response to a streamed invocation.", ex.Message);
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
            var hubConnection = new HubConnection(connection, new JsonHubProtocol(new JsonSerializer()), new LoggerFactory());
            try
            {
                await hubConnection.StartAsync();

                var invokeTask = hubConnection.Invoke<int>("Foo");

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
        public async Task StreamFailsWithExceptionWhenCompletionWithErrorReceived()
        {
            var connection = new TestConnection();
            var hubConnection = new HubConnection(connection, new JsonHubProtocol(new JsonSerializer()), new LoggerFactory());
            try
            {
                await hubConnection.StartAsync();

                var observable = hubConnection.Stream<int>("Foo");

                await connection.ReceiveJsonMessage(new { invocationId = "1", type = 3, error = "An error occurred" }).OrTimeout();

                var ex = await Assert.ThrowsAsync<HubException>(async () => await observable.Timeout(TimeSpan.FromSeconds(1)));
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
            var hubConnection = new HubConnection(connection, new JsonHubProtocol(new JsonSerializer()), new LoggerFactory());
            try
            {
                await hubConnection.StartAsync();

                var invokeTask = hubConnection.Invoke<int>("Foo");

                await connection.ReceiveJsonMessage(new { invocationId = "1", type = 2, item = 42 }).OrTimeout();

                var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => invokeTask).OrTimeout();
                Assert.Equal("Streaming methods must be invoked using HubConnection.Stream", ex.Message);
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
            var hubConnection = new HubConnection(connection, new JsonHubProtocol(new JsonSerializer()), new LoggerFactory());
            try
            {
                await hubConnection.StartAsync();

                var observable = hubConnection.Stream<string>("Foo");

                // Materialize notifications, and create a Task to force the observer to be subscribed.
                var listResult = observable.Materialize().ToList().ToTask();

                await connection.ReceiveJsonMessage(new { invocationId = "1", type = 2, item = "1" }).OrTimeout();
                await connection.ReceiveJsonMessage(new { invocationId = "1", type = 2, item = "2" }).OrTimeout();
                await connection.ReceiveJsonMessage(new { invocationId = "1", type = 2, item = "3" }).OrTimeout();
                await connection.ReceiveJsonMessage(new { invocationId = "1", type = 3 }).OrTimeout();

                var notifications = await listResult;

                Assert.Equal(new[]
                {
                    Notification.CreateOnNext("1"),
                    Notification.CreateOnNext("2"),
                    Notification.CreateOnNext("3"),
                    Notification.CreateOnCompleted<string>()
                }, notifications.ToArray());
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
            var hubConnection = new HubConnection(connection, new JsonHubProtocol(new JsonSerializer()), new LoggerFactory());
            var handlerCalled = new TaskCompletionSource<object[]>();
            try
            {
                await hubConnection.StartAsync();

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
    }
}
