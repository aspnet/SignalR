// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Protocols;
using Microsoft.AspNetCore.SignalR.Internal;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Microsoft.AspNetCore.Sockets;
using Microsoft.AspNetCore.Sockets.Client;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Microsoft.AspNetCore.SignalR.Client.Tests
{
    public partial class HubConnectionTests
    {
        [Fact]
        public async Task InvokeThrowsIfSerializingMessageFails()
        {
            var exception = new InvalidOperationException();
            var mockProtocol = MockHubProtocol.Throw(exception);
            var hubConnection = new HubConnection(() => new TestConnection(), mockProtocol, null);
            await hubConnection.StartAsync();

            var actualException =
                await Assert.ThrowsAsync<InvalidOperationException>(async () => await hubConnection.InvokeAsync<int>("test").OrTimeout());
            Assert.Same(exception, actualException);
        }

        [Fact]
        public async Task SendAsyncThrowsIfSerializingMessageFails()
        {
            var exception = new InvalidOperationException();
            var mockProtocol = MockHubProtocol.Throw(exception);
            var hubConnection = new HubConnection(() => new TestConnection(), mockProtocol, null);
            await hubConnection.StartAsync();

            var actualException =
                await Assert.ThrowsAsync<InvalidOperationException>(async () => await hubConnection.SendAsync("test"));
            Assert.Same(exception, actualException);
        }

        [Fact]
        public async Task ClosedEventRaisedWhenTheClientIsStopped()
        {
            var hubConnection = new HubConnection(() => new TestConnection(), Mock.Of<IHubProtocol>(), null);
            var closedEventTcs = new TaskCompletionSource<Exception>();
            hubConnection.Closed += e => closedEventTcs.SetResult(e);

            await hubConnection.StartAsync().OrTimeout();
            await hubConnection.StopAsync().OrTimeout();
            Assert.Null(await closedEventTcs.Task);
        }

        [Fact]
        public async Task CannotCallInvokeOnNotStartedHubConnection()
        {
            var connection = new TestConnection();
            var hubConnection = new HubConnection(() => connection, new JsonHubProtocol(), new LoggerFactory());

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => hubConnection.InvokeAsync<int>("test"));

            Assert.Equal($"The '{nameof(HubConnection.InvokeAsync)}' method cannot be called if the connection is not active", exception.Message);
        }

        [Fact]
        public async Task CannotCallInvokeOnClosedHubConnection()
        {
            var connection = new TestConnection();
            var hubConnection = new HubConnection(() => connection, new JsonHubProtocol(), new LoggerFactory());

            await hubConnection.StartAsync();
            await hubConnection.StopAsync();
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => hubConnection.InvokeAsync<int>("test"));

            Assert.Equal($"The '{nameof(HubConnection.InvokeAsync)}' method cannot be called if the connection is not active", exception.Message);
        }

        [Fact]
        public async Task CannotCallSendOnNotStartedHubConnection()
        {
            var connection = new TestConnection();
            var hubConnection = new HubConnection(() => connection, new JsonHubProtocol(), new LoggerFactory());

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => hubConnection.SendAsync("test"));

            Assert.Equal($"The '{nameof(HubConnection.SendAsync)}' method cannot be called if the connection is not active", exception.Message);
        }

        [Fact]
        public async Task CannotCallSendOnClosedHubConnection()
        {
            var connection = new TestConnection();
            var hubConnection = new HubConnection(() => connection, new JsonHubProtocol(), new LoggerFactory());

            await hubConnection.StartAsync();
            await hubConnection.StopAsync();
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => hubConnection.SendAsync("test"));

            Assert.Equal($"The '{nameof(HubConnection.SendAsync)}' method cannot be called if the connection is not active", exception.Message);
        }

        [Fact]
        public async Task CannotCallStreamOnClosedHubConnection()
        {
            var connection = new TestConnection();
            var hubConnection = new HubConnection(() => connection, new JsonHubProtocol(), new LoggerFactory());

            await hubConnection.StartAsync();
            await hubConnection.StopAsync();
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => hubConnection.StreamAsChannelAsync<int>("test"));

            Assert.Equal($"The '{nameof(HubConnection.StreamAsChannelAsync)}' method cannot be called if the connection is not active", exception.Message);
        }

        [Fact]
        public async Task CannotCallSendOnDisposedHubConnection()
        {
            var connection = new TestConnection();
            var hubConnection = new HubConnection(() => connection, new JsonHubProtocol(), new LoggerFactory());

            await hubConnection.StartAsync();
            await hubConnection.DisposeAsync();
            var exception = await Assert.ThrowsAsync<ObjectDisposedException>(() => hubConnection.SendAsync("test"));

            Assert.Equal(nameof(HubConnection), exception.ObjectName);
        }

        [Fact]
        public async Task CannotCallStreamOnDisposedHubConnection()
        {
            var connection = new TestConnection();
            var hubConnection = new HubConnection(() => connection, new JsonHubProtocol(), new LoggerFactory());

            await hubConnection.StartAsync();
            await hubConnection.DisposeAsync();
            var exception = await Assert.ThrowsAsync<ObjectDisposedException>(
                () => hubConnection.StreamAsChannelAsync<int>("test"));

            Assert.Equal(nameof(HubConnection), exception.ObjectName);
        }

        [Fact]
        public async Task CannotCallStreamOnNotStartedHubConnection()
        {
            var connection = new TestConnection();
            var hubConnection = new HubConnection(() => connection, new JsonHubProtocol(), new LoggerFactory());

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => hubConnection.StreamAsChannelAsync<int>("test"));

            Assert.Equal($"The '{nameof(HubConnection.StreamAsChannelAsync)}' method cannot be called if the connection is not active", exception.Message);
        }

        [Fact]
        public async Task PendingInvocationsAreCancelledWhenConnectionClosesCleanly()
        {
            var connection = new TestConnection();
            var hubConnection = new HubConnection(() => connection, new JsonHubProtocol(), new LoggerFactory());

            await hubConnection.StartAsync();
            var invokeTask = hubConnection.InvokeAsync<int>("testMethod");
            await hubConnection.StopAsync();

            await Assert.ThrowsAsync<TaskCanceledException>(async () => await invokeTask);
        }

        [Fact]
        public async Task PendingInvocationsAreTerminatedWithExceptionWhenConnectionClosesDueToError()
        {
            var mockConnection = new Mock<IConnection>();
            mockConnection.SetupGet(p => p.Features).Returns(new FeatureCollection());
            mockConnection
                .Setup(m => m.DisposeAsync())
                .Returns(Task.FromResult<object>(null));

            var hubConnection = new HubConnection(() => mockConnection.Object, Mock.Of<IHubProtocol>(), new LoggerFactory());

            await hubConnection.StartAsync();
            var invokeTask = hubConnection.InvokeAsync<int>("testMethod");

            var exception = new InvalidOperationException();
            mockConnection.Raise(m => m.Closed += null, mockConnection.Object, exception);

            var actualException = await Assert.ThrowsAsync<InvalidOperationException>(async () => await invokeTask);
            Assert.Equal(exception, actualException);
        }

        [Fact]
        public async Task ConnectionTerminatedIfServerTimeoutIntervalElapsesWithNoMessages()
        {
            var connection = new TestConnection();
            var hubConnection = new HubConnection(() => connection, new JsonHubProtocol(), new LoggerFactory());

            hubConnection.ServerTimeout = TimeSpan.FromMilliseconds(100);

            var closeTcs = new TaskCompletionSource<Exception>();
            hubConnection.Closed += ex => closeTcs.TrySetResult(ex);

            await hubConnection.StartAsync().OrTimeout();

            var exception = Assert.IsType<TimeoutException>(await closeTcs.Task.OrTimeout());
            Assert.Equal("Server timeout (100.00ms) elapsed without receiving a message from the server.", exception.Message);
        }

        [Fact]
        public async Task OnReceivedAfterConnectionDisposedDoesNotThrow()
        {
            var connection = new TestConnection();
            var hubConnection = new HubConnection(() => connection, new JsonHubProtocol(), new LoggerFactory());
            await hubConnection.StartAsync().OrTimeout();
            await hubConnection.StopAsync().OrTimeout();

            // Fire callbacks, they shouldn't fail
            foreach (var registration in connection.Callbacks)
            {
                await registration.InvokeAsync(new byte[0]);
            }
        }

        // Moq really doesn't handle out parameters well, so to make these tests work I added a manual mock -anurse
        private class MockHubProtocol : IHubProtocol
        {
            private HubInvocationMessage _parsed;
            private Exception _error;

            public int ParseCalls { get; private set; } = 0;
            public int WriteCalls { get; private set; } = 0;

            public static MockHubProtocol ReturnOnParse(HubInvocationMessage parsed)
            {
                return new MockHubProtocol
                {
                    _parsed = parsed
                };
            }

            public static MockHubProtocol Throw(Exception error)
            {
                return new MockHubProtocol
                {
                    _error = error
                };
            }

            public string Name => "MockHubProtocol";

            public TransferFormat TransferFormat => TransferFormat.Binary;

            public bool TryParseMessages(ReadOnlyMemory<byte> input, IInvocationBinder binder, IList<HubMessage> messages)
            {
                ParseCalls += 1;
                if (_error != null)
                {
                    throw _error;
                }
                if (_parsed != null)
                {
                    messages.Add(_parsed);
                    return true;
                }

                throw new InvalidOperationException("No Parsed Message provided");
            }

            public void WriteMessage(HubMessage message, Stream output)
            {
                WriteCalls += 1;

                if (_error != null)
                {
                    throw _error;
                }
            }
        }
    }
}
