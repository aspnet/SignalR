﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR.Internal;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Microsoft.AspNetCore.SignalR.Tests.Common;
using Microsoft.AspNetCore.Sockets.Client;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Microsoft.AspNetCore.SignalR.Client.Tests
{
    public class HubConnectionTests
    {
        [Fact]
        public async Task StartAsyncCallsConnectionStart()
        {
            var connection = new Mock<IConnection>();
            connection.SetupGet(p => p.Features).Returns(new FeatureCollection());
            connection.Setup(m => m.StartAsync()).Returns(Task.CompletedTask).Verifiable();
            var hubConnection = new HubConnection(connection.Object, Mock.Of<IHubProtocol>(), null);
            await hubConnection.StartAsync();

            connection.Verify(c => c.StartAsync(), Times.Once());
        }

        [Fact]
        public async Task DisposeAsyncCallsConnectionStart()
        {
            var connection = new Mock<IConnection>();
            connection.Setup(m => m.StartAsync()).Verifiable();
            var hubConnection = new HubConnection(connection.Object, Mock.Of<IHubProtocol>(), null);
            await hubConnection.DisposeAsync();

            connection.Verify(c => c.DisposeAsync(), Times.Once());
        }

        [Fact]
        public async Task InvokeThrowsIfSerializingMessageFails()
        {
            var exception = new InvalidOperationException();
            var mockProtocol = MockHubProtocol.Throw(exception);
            var hubConnection = new HubConnection(new TestConnection(), mockProtocol, null);
            await hubConnection.StartAsync();

            var actualException =
                await Assert.ThrowsAsync<InvalidOperationException>(async () => await hubConnection.InvokeAsync<int>("test"));
            Assert.Same(exception, actualException);
        }

        [Fact]
        public async Task ClosedEventRaisedWhenTheClientIsStopped()
        {
            var hubConnection = new HubConnection(new TestConnection(), Mock.Of<IHubProtocol>(), null);
            var closedTcs = new TaskCompletionSource<Exception>();
            _ = hubConnection.Closed.ContinueWith( (task, state) =>
            {
                var tcs = (TaskCompletionSource<Exception>)state;
                tcs.SetResult(task.Exception);
                return Task.CompletedTask;
            }, closedTcs);

            await hubConnection.StartAsync();
            await hubConnection.DisposeAsync();

            Assert.Null(await closedTcs.Task.OrTimeout());
        }

        [Fact]
        public async Task CannotCallInvokeOnClosedHubConnection()
        {
            var connection = new TestConnection();
            var hubConnection = new HubConnection(connection, new JsonHubProtocol(), new LoggerFactory());

            await hubConnection.StartAsync();
            await hubConnection.DisposeAsync();
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await hubConnection.InvokeAsync<int>("test"));

            Assert.Equal("Connection has been terminated.", exception.Message);
        }

        [Fact]
        public async Task PendingInvocationsAreCancelledWhenConnectionClosesCleanly()
        {
            var connection = new TestConnection();
            var hubConnection = new HubConnection(connection, new JsonHubProtocol(), new LoggerFactory());

            await hubConnection.StartAsync();
            var invokeTask = hubConnection.InvokeAsync<int>("testMethod");
            await hubConnection.DisposeAsync();

            await Assert.ThrowsAsync<TaskCanceledException>(async () => await invokeTask);
        }

        //[Fact]
        //public async Task PendingInvocationsAreTerminatedWithExceptionWhenConnectionClosesDueToError()
        //{
        //    var exception = new InvalidOperationException();
        //    var mockConnection = new Mock<IConnection>();
        //    mockConnection.SetupGet(p => p.Features).Returns(new FeatureCollection());
        //    mockConnection
        //        .Setup(m => m.DisposeAsync())
        //        .Callback(async () => await mockConnection.Object.Closed)
        //        .Returns(Task.FromResult<object>(null));

        //    var hubConnection = new HubConnection(mockConnection.Object, Mock.Of<IHubProtocol>(), new LoggerFactory());

        //    await hubConnection.StartAsync().OrTimeout();
        //    var invokeTask = hubConnection.InvokeAsync<int>("testMethod");
        //    await hubConnection.DisposeAsync().OrTimeout();

        //    var thrown = await Assert.ThrowsAsync(exception.GetType(), async () => await invokeTask);
        //    Assert.Same(exception, thrown);
        //}

        // Moq really doesn't handle out parameters well, so to make these tests work I added a manual mock -anurse
        private class MockHubProtocol : IHubProtocol
        {
            private HubMessage _parsed;
            private Exception _error;

            public int ParseCalls { get; private set; } = 0;
            public int WriteCalls { get; private set; } = 0;

            public static MockHubProtocol ReturnOnParse(HubMessage parsed)
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

            public ProtocolType Type => ProtocolType.Binary;

            public bool TryParseMessages(ReadOnlyBuffer<byte> input, IInvocationBinder binder, out IList<HubMessage> messages)
            {
                messages = new List<HubMessage>();

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
