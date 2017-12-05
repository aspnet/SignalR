// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Client.Tests;
using Microsoft.AspNetCore.Sockets.Client.Http;
using Microsoft.AspNetCore.Sockets.Features;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Xunit;

namespace Microsoft.AspNetCore.Sockets.Client.Tests
{
    public class HttpConnectionTests
    {
        [Fact]
        public void CannotCreateConnectionWithNullUrl()
        {
            var exception = Assert.Throws<ArgumentNullException>(() => new HttpConnection(null));
            Assert.Equal("url", exception.ParamName);
        }

        [Fact]
        public void ConnectionReturnsUrlUsedToStartTheConnection()
        {
            var connectionUrl = new Uri("http://fakeuri.org/");
            Assert.Equal(connectionUrl, new HttpConnection(connectionUrl).Url);
        }

        [Theory]
        [InlineData((TransportType)0)]
        [InlineData(TransportType.All + 1)]
        public void CannotStartConnectionWithInvalidTransportType(TransportType requestedTransportType)
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new HttpConnection(new Uri("http://fakeuri.org/"), requestedTransportType));
        }

        [Fact]
        public async Task CannotStartRunningConnection()
        {
            var mockHttpHandler = new Mock<HttpMessageHandler>();
            mockHttpHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .Returns<HttpRequestMessage, CancellationToken>(async (request, cancellationToken) =>
                {
                    await Task.Yield();

                    return IsNegotiateRequest(request)
                        ? ResponseUtils.CreateResponse(HttpStatusCode.OK, ResponseUtils.CreateNegotiationResponse())
                        : ResponseUtils.CreateResponse(HttpStatusCode.OK);
                });

            var connection = new HttpConnection(new Uri("http://fakeuri.org/"), TransportType.LongPolling, loggerFactory: null,
                httpOptions: new HttpOptions { HttpMessageHandler = mockHttpHandler.Object });
            try
            {
                await connection.StartAsync();
                var exception =
                    await Assert.ThrowsAsync<InvalidOperationException>(
                        async () => await connection.StartAsync());
                Assert.Equal("Cannot start a connection that is not in the Initial state.", exception.Message);
            }
            finally
            {
                await connection.DisposeAsync();
            }
        }

        [Fact]
        public async Task CannotStartStoppedConnection()
        {
            var mockHttpHandler = new Mock<HttpMessageHandler>();
            mockHttpHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .Returns<HttpRequestMessage, CancellationToken>(async (request, cancellationToken) =>
                {
                    await Task.Yield();
                    return IsNegotiateRequest(request)
                        ? ResponseUtils.CreateResponse(HttpStatusCode.OK, ResponseUtils.CreateNegotiationResponse())
                        : ResponseUtils.CreateResponse(HttpStatusCode.OK);
                });

            var connection = new HttpConnection(new Uri("http://fakeuri.org/"), TransportType.LongPolling, loggerFactory: null,
                httpOptions: new HttpOptions { HttpMessageHandler = mockHttpHandler.Object });

            await connection.StartAsync();
            await connection.DisposeAsync();
            var exception =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await connection.StartAsync());

            Assert.Equal("Cannot start a connection that is not in the Initial state.", exception.Message);
        }

        [Fact]
        public async Task CannotStartDisposedConnection()
        {
            using (var httpClient = new HttpClient())
            {
                var connection = new HttpConnection(new Uri("http://fakeuri.org/"));
                await connection.DisposeAsync();
                var exception =
                    await Assert.ThrowsAsync<InvalidOperationException>(
                        async () => await connection.StartAsync());

                Assert.Equal("Cannot start a connection that is not in the Initial state.", exception.Message);
            }
        }

        [Fact]
        public async Task CanStopStartingConnection()
        {
            // Used to make sure StartAsync is not completed before DisposeAsync is called
            var releaseNegotiateTcs = new TaskCompletionSource<object>();
            // Used to make sure that DisposeAsync runs after we check the state in StartAsync
            var allowDisposeTcs = new TaskCompletionSource<object>();
            // Used to make sure that DisposeAsync continues only after StartAsync finished
            var releaseDisposeTcs = new TaskCompletionSource<object>();

            var mockHttpHandler = new Mock<HttpMessageHandler>();
            mockHttpHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .Returns<HttpRequestMessage, CancellationToken>(async (request, cancellationToken) =>
                {
                    await Task.Yield();
                    // allow DisposeAsync to continue once we know we are past the connection state check
                    allowDisposeTcs.SetResult(null);
                    await releaseNegotiateTcs.Task;
                    return IsNegotiateRequest(request)
                        ? ResponseUtils.CreateResponse(HttpStatusCode.OK, ResponseUtils.CreateNegotiationResponse())
                        : ResponseUtils.CreateResponse(HttpStatusCode.OK);
                });

            var transport = new Mock<ITransport>();
            transport.Setup(t => t.StopAsync()).Returns(async () => { await releaseDisposeTcs.Task; });
            var connection = new HttpConnection(new Uri("http://fakeuri.org/"), new TestTransportFactory(transport.Object), loggerFactory: null,
                httpOptions: new HttpOptions { HttpMessageHandler = mockHttpHandler.Object });

            var startTask = connection.StartAsync();
            await allowDisposeTcs.Task;
            var disposeTask = connection.DisposeAsync();
            // allow StartAsync to continue once DisposeAsync has started
            releaseNegotiateTcs.SetResult(null);

            // unblock DisposeAsync only after StartAsync completed
            await startTask.OrTimeout();
            releaseDisposeTcs.SetResult(null);
            await disposeTask.OrTimeout();

            transport.Verify(t => t.StartAsync(It.IsAny<Uri>(), It.IsAny<Channel<byte[], SendMessage>>(), It.IsAny<TransferMode>(), It.IsAny<string>(), It.IsAny<IConnection>()), Times.Never);
        }

        [Fact]
        public async Task SendThrowsIfConnectionIsNotStarted()
        {
            var connection = new HttpConnection(new Uri("http://fakeuri.org/"));
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await connection.SendAsync(new byte[0]));
            Assert.Equal("Cannot send messages when the connection is not in the Connected state.", exception.Message);
        }

        [Fact]
        public async Task SendThrowsIfConnectionIsDisposed()
        {
            var mockHttpHandler = new Mock<HttpMessageHandler>();
            mockHttpHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .Returns<HttpRequestMessage, CancellationToken>(async (request, cancellationToken) =>
                {
                    await Task.Yield();
                    return IsNegotiateRequest(request)
                        ? ResponseUtils.CreateResponse(HttpStatusCode.OK, ResponseUtils.CreateNegotiationResponse())
                        : ResponseUtils.CreateResponse(HttpStatusCode.OK);
                });

            var connection = new HttpConnection(new Uri("http://fakeuri.org/"), TransportType.LongPolling, loggerFactory: null,
                httpOptions: new HttpOptions { HttpMessageHandler = mockHttpHandler.Object });

            await connection.StartAsync();
            await connection.DisposeAsync();

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await connection.SendAsync(new byte[0]));
            Assert.Equal("Cannot send messages when the connection is not in the Connected state.", exception.Message);
        }

        [Fact]
        public async Task ClosedEventRaisedWhenTheClientIsBeingStopped()
        {
            var mockHttpHandler = new Mock<HttpMessageHandler>();
            mockHttpHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .Returns<HttpRequestMessage, CancellationToken>(async (request, cancellationToken) =>
                {
                    await Task.Yield();
                    return IsNegotiateRequest(request)
                        ? ResponseUtils.CreateResponse(HttpStatusCode.OK, ResponseUtils.CreateNegotiationResponse())
                        : ResponseUtils.CreateResponse(HttpStatusCode.OK);
                });

            var connection = new HttpConnection(new Uri("http://fakeuri.org/"), TransportType.LongPolling, loggerFactory: null,
                httpOptions: new HttpOptions { HttpMessageHandler = mockHttpHandler.Object });


            await connection.StartAsync().OrTimeout();
            await connection.DisposeAsync().OrTimeout();
            await connection.Closed.OrTimeout();
            // in case of clean disconnect error should be null
        }

        [Fact]
        public async Task ClosedEventRaisedWhenConnectionToServerLost()
        {
            var mockHttpHandler = new Mock<HttpMessageHandler>();
            mockHttpHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .Returns<HttpRequestMessage, CancellationToken>(async (request, cancellationToken) =>
                {
                    await Task.Yield();

                    return request.Method == HttpMethod.Get
                        ? ResponseUtils.CreateResponse(HttpStatusCode.InternalServerError)
                        : IsNegotiateRequest(request)
                            ? ResponseUtils.CreateResponse(HttpStatusCode.OK, ResponseUtils.CreateNegotiationResponse())
                            : ResponseUtils.CreateResponse(HttpStatusCode.OK);
                });

            var connection = new HttpConnection(new Uri("http://fakeuri.org/"), TransportType.LongPolling, loggerFactory: null,
                httpOptions: new HttpOptions { HttpMessageHandler = mockHttpHandler.Object });

            try
            {
                await connection.StartAsync().OrTimeout();
                await Assert.ThrowsAsync<HttpRequestException>(() => connection.Closed.OrTimeout());
            }
            finally
            {
                await connection.DisposeAsync();
            }
        }

        [Fact]
        public async Task ReceivedCallbackNotRaisedAfterConnectionIsDisposed()
        {
            var mockHttpHandler = new Mock<HttpMessageHandler>();
            mockHttpHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .Returns<HttpRequestMessage, CancellationToken>(async (request, cancellationToken) =>
                {
                    await Task.Yield();
                    return IsNegotiateRequest(request)
                        ? ResponseUtils.CreateResponse(HttpStatusCode.OK, ResponseUtils.CreateNegotiationResponse())
                        : ResponseUtils.CreateResponse(HttpStatusCode.OK);
                });

            var mockTransport = new Mock<ITransport>();
            Channel<byte[], SendMessage> channel = null;
            mockTransport.Setup(t => t.StartAsync(It.IsAny<Uri>(), It.IsAny<Channel<byte[], SendMessage>>(), It.IsAny<TransferMode>(), It.IsAny<string>(), It.IsAny<IConnection>()))
                .Returns<Uri, Channel<byte[], SendMessage>, TransferMode, string>((url, c, transferMode, connectionId) =>
                {
                    channel = c;
                    return Task.CompletedTask;
                });
            mockTransport.Setup(t => t.StopAsync())
                .Returns(() =>
                {
                    // The connection is now in the Disconnected state so the Received event for
                    // this message should not be raised
                    channel.Writer.TryWrite(Array.Empty<byte>());
                    channel.Writer.TryComplete();
                    return Task.CompletedTask;
                });
            mockTransport.SetupGet(t => t.Mode).Returns(TransferMode.Text);

            var connection = new HttpConnection(new Uri("http://fakeuri.org/"), new TestTransportFactory(mockTransport.Object), loggerFactory: null,
                httpOptions: new HttpOptions { HttpMessageHandler = mockHttpHandler.Object });

            var onReceivedInvoked = false;
            connection.OnReceived(_ =>
            {
                onReceivedInvoked = true;
                return Task.CompletedTask;
            });

            await connection.StartAsync();
            await connection.DisposeAsync();
            Assert.False(onReceivedInvoked);
        }

        [Fact]
        public async Task EventsAreNotRunningOnMainLoop()
        {
            var mockHttpHandler = new Mock<HttpMessageHandler>();
            mockHttpHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .Returns<HttpRequestMessage, CancellationToken>(async (request, cancellationToken) =>
                {
                    await Task.Yield();
                    return IsNegotiateRequest(request)
                        ? ResponseUtils.CreateResponse(HttpStatusCode.OK, ResponseUtils.CreateNegotiationResponse())
                        : ResponseUtils.CreateResponse(HttpStatusCode.OK);
                });

            var mockTransport = new Mock<ITransport>();
            Channel<byte[], SendMessage> channel = null;
            mockTransport.Setup(t => t.StartAsync(It.IsAny<Uri>(), It.IsAny<Channel<byte[], SendMessage>>(), It.IsAny<TransferMode>(), It.IsAny<string>(), It.IsAny<IConnection>()))
                .Returns<Uri, Channel<byte[], SendMessage>, TransferMode, string>((url, c, transferMode, connectionId) =>
                {
                    channel = c;
                    return Task.CompletedTask;
                });
            mockTransport.Setup(t => t.StopAsync())
                .Returns(() =>
                {
                    channel.Writer.TryComplete();
                    return Task.CompletedTask;
                });
            mockTransport.SetupGet(t => t.Mode).Returns(TransferMode.Text);

            var callbackInvokedTcs = new TaskCompletionSource<object>();
            var closedTcs = new TaskCompletionSource<object>();

            var connection = new HttpConnection(new Uri("http://fakeuri.org/"), new TestTransportFactory(mockTransport.Object), loggerFactory: null,
                httpOptions: new HttpOptions { HttpMessageHandler = mockHttpHandler.Object });

            connection.OnReceived(_ =>
                {
                    callbackInvokedTcs.SetResult(null);
                    return closedTcs.Task;
                });

            await connection.StartAsync();
            channel.Writer.TryWrite(Array.Empty<byte>());

            // Ensure that the Received callback has been called before attempting the second write
            await callbackInvokedTcs.Task.OrTimeout();
            channel.Writer.TryWrite(Array.Empty<byte>());

            // Ensure that SignalR isn't blocked by the receive callback
            Assert.False(channel.Reader.TryRead(out var message));

            closedTcs.SetResult(null);

            await connection.DisposeAsync();
        }

        [Fact]
        public async Task EventQueueTimeout()
        {
            var mockHttpHandler = new Mock<HttpMessageHandler>();
            mockHttpHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .Returns<HttpRequestMessage, CancellationToken>(async (request, cancellationToken) =>
                {
                    await Task.Yield();
                    return IsNegotiateRequest(request)
                        ? ResponseUtils.CreateResponse(HttpStatusCode.OK, ResponseUtils.CreateNegotiationResponse())
                        : ResponseUtils.CreateResponse(HttpStatusCode.OK);
                });

            var mockTransport = new Mock<ITransport>();
            Channel<byte[], SendMessage> channel = null;
            mockTransport.Setup(t => t.StartAsync(It.IsAny<Uri>(), It.IsAny<Channel<byte[], SendMessage>>(), It.IsAny<TransferMode>(), It.IsAny<string>(), It.IsAny<IConnection>()))
                .Returns<Uri, Channel<byte[], SendMessage>, TransferMode, string>((url, c, transferMode, connectionId) =>
                {
                    channel = c;
                    return Task.CompletedTask;
                });
            mockTransport.Setup(t => t.StopAsync())
                .Returns(() =>
                {
                    channel.Writer.TryComplete();
                    return Task.CompletedTask;
                });
            mockTransport.SetupGet(t => t.Mode).Returns(TransferMode.Text);

            var blockReceiveCallbackTcs = new TaskCompletionSource<object>();

            var connection = new HttpConnection(new Uri("http://fakeuri.org/"), new TestTransportFactory(mockTransport.Object), loggerFactory: null,
                httpOptions: new HttpOptions { HttpMessageHandler = mockHttpHandler.Object });
            connection.OnReceived(_ => blockReceiveCallbackTcs.Task);

            await connection.StartAsync();
            channel.Writer.TryWrite(Array.Empty<byte>());

            // Ensure that SignalR isn't blocked by the receive callback
            Assert.False(channel.Reader.TryRead(out var message));

            await connection.DisposeAsync();
        }

        [Fact]
        public async Task EventQueueTimeoutWithException()
        {
            var mockHttpHandler = new Mock<HttpMessageHandler>();
            mockHttpHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .Returns<HttpRequestMessage, CancellationToken>(async (request, cancellationToken) =>
                {
                    await Task.Yield();
                    return IsNegotiateRequest(request)
                        ? ResponseUtils.CreateResponse(HttpStatusCode.OK, ResponseUtils.CreateNegotiationResponse())
                        : ResponseUtils.CreateResponse(HttpStatusCode.OK);
                });

            var mockTransport = new Mock<ITransport>();
            Channel<byte[], SendMessage> channel = null;
            mockTransport.Setup(t => t.StartAsync(It.IsAny<Uri>(), It.IsAny<Channel<byte[], SendMessage>>(), It.IsAny<TransferMode>(), It.IsAny<string>(), It.IsAny<IConnection>()))
                .Returns<Uri, Channel<byte[], SendMessage>, TransferMode, string>((url, c, transferMode, connectionId) =>
                {
                    channel = c;
                    return Task.CompletedTask;
                });
            mockTransport.Setup(t => t.StopAsync())
                .Returns(() =>
                {
                    channel.Writer.TryComplete();
                    return Task.CompletedTask;
                });
            mockTransport.SetupGet(t => t.Mode).Returns(TransferMode.Text);

            var callbackInvokedTcs = new TaskCompletionSource<object>();

            var connection = new HttpConnection(new Uri("http://fakeuri.org/"), new TestTransportFactory(mockTransport.Object), loggerFactory: null,
                httpOptions: new HttpOptions { HttpMessageHandler = mockHttpHandler.Object });
            connection.OnReceived(_ =>
            {
                throw new OperationCanceledException();
            });

            await connection.StartAsync();
            channel.Writer.TryWrite(Array.Empty<byte>());

            // Ensure that SignalR isn't blocked by the receive callback
            Assert.False(channel.Reader.TryRead(out var message));

            await connection.DisposeAsync();
        }

        [Fact]
        public async Task ClosedEventNotRaisedWhenTheClientIsStoppedButWasNeverStarted()
        {
            var connection = new HttpConnection(new Uri("http://fakeuri.org/"));

            await connection.DisposeAsync();
            Assert.False(connection.Closed.IsCompleted);
        }

        [Fact]
        public async Task TransportIsStoppedWhenConnectionIsStopped()
        {
            var mockHttpHandler = new Mock<HttpMessageHandler>();
            mockHttpHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .Returns<HttpRequestMessage, CancellationToken>(async (request, cancellationToken) =>
                {
                    await Task.Yield();

                    return IsNegotiateRequest(request)
                        ? ResponseUtils.CreateResponse(HttpStatusCode.OK, ResponseUtils.CreateNegotiationResponse())
                        : ResponseUtils.CreateResponse(HttpStatusCode.OK);
                });

            using (var httpClient = new HttpClient(mockHttpHandler.Object))
            {
                var longPollingTransport = new LongPollingTransport(httpClient, null, new LoggerFactory());
                var connection = new HttpConnection(new Uri("http://fakeuri.org/"), new TestTransportFactory(longPollingTransport), loggerFactory: null,
                    httpOptions: new HttpOptions { HttpMessageHandler = mockHttpHandler.Object });

                try
                {
                    await connection.StartAsync();

                    Assert.False(longPollingTransport.Running.IsCompleted);
                }
                finally
                {
                    await connection.DisposeAsync();
                }

                await longPollingTransport.Running.OrTimeout();
            }
        }

        [Fact]
        public async Task CanSendData()
        {
            var data = new byte[] { 1, 1, 2, 3, 5, 8 };

            var sendTcs = new TaskCompletionSource<byte[]>();
            var mockHttpHandler = new Mock<HttpMessageHandler>();
            mockHttpHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .Returns<HttpRequestMessage, CancellationToken>(async (request, cancellationToken) =>
                {
                    await Task.Yield();
                    if (IsNegotiateRequest(request))
                    {
                        return ResponseUtils.CreateResponse(HttpStatusCode.OK, ResponseUtils.CreateNegotiationResponse());
                    }

                    if (request.Method == HttpMethod.Post)
                    {
                        sendTcs.SetResult(await request.Content.ReadAsByteArrayAsync());
                    }

                    return ResponseUtils.CreateResponse(HttpStatusCode.OK);
                });

            var connection = new HttpConnection(new Uri("http://fakeuri.org/"), TransportType.LongPolling, loggerFactory: null,
                httpOptions: new HttpOptions { HttpMessageHandler = mockHttpHandler.Object });
            try
            {
                await connection.StartAsync();

                await connection.SendAsync(data);

                Assert.Equal(data, await sendTcs.Task.OrTimeout());
            }
            finally
            {
                await connection.DisposeAsync();
            }
        }

        [Fact]
        public async Task SendAsyncThrowsIfConnectionIsNotStarted()
        {
            var connection = new HttpConnection(new Uri("http://fakeuri.org/"));
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await connection.SendAsync(new byte[0]));

            Assert.Equal("Cannot send messages when the connection is not in the Connected state.", exception.Message);
        }

        [Fact]
        public async Task SendAsyncThrowsIfConnectionIsDisposed()
        {
            var mockHttpHandler = new Mock<HttpMessageHandler>();
            mockHttpHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .Returns<HttpRequestMessage, CancellationToken>(async (request, cancellationToken) =>
                {
                    await Task.Yield();

                    var content = string.Empty;
                    if (request.Method == HttpMethod.Get)
                    {
                        content = "T2:T:42;";
                    }

                    return IsNegotiateRequest(request)
                        ? ResponseUtils.CreateResponse(HttpStatusCode.OK, ResponseUtils.CreateNegotiationResponse())
                        : ResponseUtils.CreateResponse(HttpStatusCode.OK, content);
                });

            var connection = new HttpConnection(new Uri("http://fakeuri.org/"), TransportType.LongPolling, loggerFactory: null,
                httpOptions: new HttpOptions { HttpMessageHandler = mockHttpHandler.Object });

            await connection.StartAsync();
            await connection.DisposeAsync();

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await connection.SendAsync(new byte[0]));

            Assert.Equal("Cannot send messages when the connection is not in the Connected state.", exception.Message);
        }

        [Fact]
        public async Task CallerReceivesExceptionsFromSendAsync()
        {
            var mockHttpHandler = new Mock<HttpMessageHandler>();
            mockHttpHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .Returns<HttpRequestMessage, CancellationToken>(async (request, cancellationToken) =>
                {
                    await Task.Yield();

                    return IsNegotiateRequest(request)
                        ? ResponseUtils.CreateResponse(HttpStatusCode.OK, ResponseUtils.CreateNegotiationResponse())
                        : request.Method == HttpMethod.Post
                            ? ResponseUtils.CreateResponse(HttpStatusCode.InternalServerError)
                            : ResponseUtils.CreateResponse(HttpStatusCode.OK);
                });

            var connection = new HttpConnection(new Uri("http://fakeuri.org/"), TransportType.LongPolling, loggerFactory: null,
                httpOptions: new HttpOptions { HttpMessageHandler = mockHttpHandler.Object });
            await connection.StartAsync();

            var exception = await Assert.ThrowsAsync<HttpRequestException>(
                async () => await connection.SendAsync(new byte[0]));

            await connection.DisposeAsync();
        }

        [Fact]
        public async Task CanReceiveData()
        {
            var mockHttpHandler = new Mock<HttpMessageHandler>();
            mockHttpHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .Returns<HttpRequestMessage, CancellationToken>(async (request, cancellationToken) =>
                {
                    await Task.Yield();

                    var content = string.Empty;

                    if (request.Method == HttpMethod.Get)
                    {
                        content = "42";
                    }

                    return IsNegotiateRequest(request)
                        ? ResponseUtils.CreateResponse(HttpStatusCode.OK, ResponseUtils.CreateNegotiationResponse())
                        : ResponseUtils.CreateResponse(HttpStatusCode.OK, content);
                });

            var connection = new HttpConnection(new Uri("http://fakeuri.org/"), TransportType.LongPolling, loggerFactory: null,
                httpOptions: new HttpOptions { HttpMessageHandler = mockHttpHandler.Object });
            try
            {
                var receiveTcs = new TaskCompletionSource<string>();
                connection.OnReceived((data, state) =>
                {
                    var tcs = ((TaskCompletionSource<string>)state);
                    tcs.TrySetResult(Encoding.UTF8.GetString(data));
                    return Task.CompletedTask;
                }, receiveTcs);

                _ = connection.Closed.ContinueWith(task =>
                {
                    if (task.Exception != null)
                    {
                        receiveTcs.TrySetException(task.Exception);
                    }
                    else
                    {
                        receiveTcs.TrySetCanceled();
                    }
                    return Task.CompletedTask;
                });

                await connection.StartAsync().OrTimeout();
                Assert.Equal("42", await receiveTcs.Task.OrTimeout());
            }
            finally
            {
                await connection.DisposeAsync();
            }
        }

        [Fact]
        public async Task CanReceiveDataEvenIfExceptionThrownFromPreviousReceivedEvent()
        {
            var mockHttpHandler = new Mock<HttpMessageHandler>();
            mockHttpHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .Returns<HttpRequestMessage, CancellationToken>(async (request, cancellationToken) =>
                {
                    await Task.Yield();

                    var content = string.Empty;

                    if (request.Method == HttpMethod.Get)
                    {
                        content = "42";
                    }

                    return IsNegotiateRequest(request)
                        ? ResponseUtils.CreateResponse(HttpStatusCode.OK, ResponseUtils.CreateNegotiationResponse())
                        : ResponseUtils.CreateResponse(HttpStatusCode.OK, content);
                });

            var connection = new HttpConnection(new Uri("http://fakeuri.org/"), TransportType.LongPolling, loggerFactory: null,
                httpOptions: new HttpOptions { HttpMessageHandler = mockHttpHandler.Object });
            try
            {
                var receiveTcs = new TaskCompletionSource<string>();

                var receivedRaised = false;
                connection.OnReceived(data =>
                {
                    if (!receivedRaised)
                    {
                        receivedRaised = true;
                        return Task.FromException(new InvalidOperationException());
                    }

                    receiveTcs.TrySetResult(Encoding.UTF8.GetString(data));
                    return Task.CompletedTask;
                });

                _ = connection.Closed.ContinueWith(task =>
                {
                    if (task.Exception != null)
                    {
                        receiveTcs.TrySetException(task.Exception);
                    }
                    else
                    {
                        receiveTcs.TrySetCanceled();
                    }
                    return Task.CompletedTask;
                });

                await connection.StartAsync();

                Assert.Equal("42", await receiveTcs.Task.OrTimeout());
            }
            finally
            {
                await connection.DisposeAsync();
            }
        }

        [Fact]
        public async Task CanReceiveDataEvenIfExceptionThrownSynchronouslyFromPreviousReceivedEvent()
        {
            var mockHttpHandler = new Mock<HttpMessageHandler>();
            mockHttpHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .Returns<HttpRequestMessage, CancellationToken>(async (request, cancellationToken) =>
                {
                    await Task.Yield();

                    var content = string.Empty;

                    if (request.Method == HttpMethod.Get)
                    {
                        content = "42";
                    }

                    return IsNegotiateRequest(request)
                        ? ResponseUtils.CreateResponse(HttpStatusCode.OK, ResponseUtils.CreateNegotiationResponse())
                        : ResponseUtils.CreateResponse(HttpStatusCode.OK, content);
                });

            var connection = new HttpConnection(new Uri("http://fakeuri.org/"), TransportType.LongPolling, loggerFactory: null,
                httpOptions: new HttpOptions { HttpMessageHandler = mockHttpHandler.Object });
            try
            {
                var receiveTcs = new TaskCompletionSource<string>();

                var receivedRaised = false;
                connection.OnReceived((data) =>
                {
                    if (!receivedRaised)
                    {
                        receivedRaised = true;
                        throw new InvalidOperationException();
                    }

                    receiveTcs.TrySetResult(Encoding.UTF8.GetString(data));
                    return Task.CompletedTask;
                });

                _ = connection.Closed.ContinueWith(task =>
                {
                    if (task.Exception != null)
                    {
                        receiveTcs.TrySetException(task.Exception);
                    }
                    else
                    {
                        receiveTcs.TrySetCanceled();
                    }
                    return Task.CompletedTask;
                });

                await connection.StartAsync();

                Assert.Equal("42", await receiveTcs.Task.OrTimeout());
            }
            finally
            {
                await connection.DisposeAsync();
            }
        }

        [Fact]
        public async Task CannotSendAfterReceiveThrewException()
        {
            var mockHttpHandler = new Mock<HttpMessageHandler>();
            mockHttpHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .Returns<HttpRequestMessage, CancellationToken>(async (request, cancellationToken) =>
                {
                    await Task.Yield();

                    return request.Method == HttpMethod.Get
                        ? ResponseUtils.CreateResponse(HttpStatusCode.InternalServerError)
                        : IsNegotiateRequest(request)
                            ? ResponseUtils.CreateResponse(HttpStatusCode.OK, ResponseUtils.CreateNegotiationResponse())
                            : ResponseUtils.CreateResponse(HttpStatusCode.OK);
                });

            var connection = new HttpConnection(new Uri("http://fakeuri.org/"), TransportType.LongPolling, loggerFactory: null,
                httpOptions: new HttpOptions { HttpMessageHandler = mockHttpHandler.Object });
            try
            {

                await connection.StartAsync().OrTimeout();

                // Exception in send should shutdown the connection
                await Assert.ThrowsAsync<HttpRequestException>(() => connection.Closed.OrTimeout());

                var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => connection.SendAsync(new byte[0]));

                Assert.Equal("Cannot send messages when the connection is not in the Connected state.", exception.Message);
            }
            finally
            {
                await connection.DisposeAsync();
            }
        }

        [Theory]
        [InlineData("")]
        [InlineData("Not Json")]
        public async Task StartThrowsFormatExceptionIfNegotiationResponseIsInvalid(string negotiatePayload)
        {
            var mockHttpHandler = new Mock<HttpMessageHandler>();
            mockHttpHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .Returns<HttpRequestMessage, CancellationToken>(async (request, cancellationToken) =>
                {
                    await Task.Yield();
                    return ResponseUtils.CreateResponse(HttpStatusCode.OK, negotiatePayload);
                });

            var connection = new HttpConnection(new Uri("http://fakeuri.org/"), TransportType.LongPolling, loggerFactory: null,
                httpOptions: new HttpOptions { HttpMessageHandler = mockHttpHandler.Object });
            var exception = await Assert.ThrowsAsync<FormatException>(
                () => connection.StartAsync());

            Assert.Equal("Invalid negotiation response received.", exception.Message);
        }

        [Fact]
        public async Task StartThrowsFormatExceptionIfNegotiationResponseHasNoConnectionId()
        {
            var mockHttpHandler = new Mock<HttpMessageHandler>();
            mockHttpHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .Returns<HttpRequestMessage, CancellationToken>(async (request, cancellationToken) =>
                {
                    await Task.Yield();
                    return ResponseUtils.CreateResponse(HttpStatusCode.OK,
                        ResponseUtils.CreateNegotiationResponse(connectionId: null));
                });

            var connection = new HttpConnection(new Uri("http://fakeuri.org/"), TransportType.LongPolling, loggerFactory: null,
                httpOptions: new HttpOptions { HttpMessageHandler = mockHttpHandler.Object });
            var exception = await Assert.ThrowsAsync<FormatException>(
                () => connection.StartAsync());

            Assert.Equal("Invalid connection id returned in negotiation response.", exception.Message);
        }

        [Fact]
        public async Task StartThrowsFormatExceptionIfNegotiationResponseHasNoTransports()
        {
            var mockHttpHandler = new Mock<HttpMessageHandler>();
            mockHttpHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .Returns<HttpRequestMessage, CancellationToken>(async (request, cancellationToken) =>
                {
                    await Task.Yield();
                    return ResponseUtils.CreateResponse(HttpStatusCode.OK,
                        ResponseUtils.CreateNegotiationResponse(transportTypes: null));
                });

            var connection = new HttpConnection(new Uri("http://fakeuri.org/"), TransportType.LongPolling, loggerFactory: null,
                httpOptions: new HttpOptions { HttpMessageHandler = mockHttpHandler.Object });
            var exception = await Assert.ThrowsAsync<FormatException>(
                () => connection.StartAsync());

            Assert.Equal("No transports returned in negotiation response.", exception.Message);
        }

        [Theory]
        [InlineData((TransportType)0)]
        [InlineData(TransportType.ServerSentEvents)]
        public async Task ConnectionCannotBeStartedIfNoCommonTransportsBetweenClientAndServer(TransportType serverTransports)
        {
            var mockHttpHandler = new Mock<HttpMessageHandler>();
            mockHttpHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .Returns<HttpRequestMessage, CancellationToken>(async (request, cancellationToken) =>
                {
                    await Task.Yield();
                    return ResponseUtils.CreateResponse(HttpStatusCode.OK,
                        ResponseUtils.CreateNegotiationResponse(transportTypes: serverTransports));
                });

            var connection = new HttpConnection(new Uri("http://fakeuri.org/"), TransportType.LongPolling, loggerFactory: null,
                httpOptions: new HttpOptions { HttpMessageHandler = mockHttpHandler.Object });
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => connection.StartAsync());

            Assert.Equal("No requested transports available on the server.", exception.Message);
        }

        [Fact]
        public async Task CanStartConnectionWithoutSettingTransferModeFeature()
        {
            var mockHttpHandler = new Mock<HttpMessageHandler>();
            mockHttpHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .Returns<HttpRequestMessage, CancellationToken>(async (request, cancellationToken) =>
                {
                    await Task.Yield();
                    return IsNegotiateRequest(request)
                        ? ResponseUtils.CreateResponse(HttpStatusCode.OK, ResponseUtils.CreateNegotiationResponse())
                        : ResponseUtils.CreateResponse(HttpStatusCode.OK);
                });

            var mockTransport = new Mock<ITransport>();
            Channel<byte[], SendMessage> channel = null;
            mockTransport.Setup(t => t.StartAsync(It.IsAny<Uri>(), It.IsAny<Channel<byte[], SendMessage>>(), It.IsAny<TransferMode>(), It.IsAny<string>(), It.IsAny<IConnection>()))
                .Returns<Uri, Channel<byte[], SendMessage>, TransferMode, string>((url, c, transferMode, connectionId) =>
                {
                    channel = c;
                    return Task.CompletedTask;
                });
            mockTransport.Setup(t => t.StopAsync())
                .Returns(() =>
                {
                    channel.Writer.TryComplete();
                    return Task.CompletedTask;
                });
            mockTransport.SetupGet(t => t.Mode).Returns(TransferMode.Binary);

            var connection = new HttpConnection(new Uri("http://fakeuri.org/"), new TestTransportFactory(mockTransport.Object),
                loggerFactory: null, httpOptions: new HttpOptions { HttpMessageHandler = mockHttpHandler.Object });

            await connection.StartAsync().OrTimeout();
            var transferModeFeature = connection.Features.Get<ITransferModeFeature>();
            await connection.DisposeAsync().OrTimeout();

            mockTransport.Verify(t => t.StartAsync(
                It.IsAny<Uri>(), It.IsAny<Channel<byte[], SendMessage>>(), TransferMode.Text, It.IsAny<string>(), It.IsAny<IConnection>()), Times.Once);
            Assert.NotNull(transferModeFeature);
            Assert.Equal(TransferMode.Binary, transferModeFeature.TransferMode);
        }

        [Theory]
        [InlineData("http://fakeuri.org/", "http://fakeuri.org/negotiate")]
        [InlineData("http://fakeuri.org/?q=1/0", "http://fakeuri.org/negotiate?q=1/0")]
        [InlineData("http://fakeuri.org?q=1/0", "http://fakeuri.org/negotiate?q=1/0")]
        [InlineData("http://fakeuri.org/endpoint", "http://fakeuri.org/endpoint/negotiate")]
        [InlineData("http://fakeuri.org/endpoint/", "http://fakeuri.org/endpoint/negotiate")]
        [InlineData("http://fakeuri.org/endpoint?q=1/0", "http://fakeuri.org/endpoint/negotiate?q=1/0")]
        public async Task CorrectlyHandlesQueryStringWhenAppendingNegotiateToUrl(string requested, string expectedNegotiate)
        {
            var mockHttpHandler = new Mock<HttpMessageHandler>();
            mockHttpHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .Returns<HttpRequestMessage, CancellationToken>(async (request, cancellationToken) =>
                {
                    await Task.Yield();
                    Assert.Equal(expectedNegotiate, request.RequestUri.ToString());
                    return ResponseUtils.CreateResponse(HttpStatusCode.OK,
                        ResponseUtils.CreateNegotiationResponse());
                });

            var connection = new HttpConnection(new Uri(requested), TransportType.LongPolling, loggerFactory: null,
                httpOptions: new HttpOptions { HttpMessageHandler = mockHttpHandler.Object });
            await connection.StartAsync().OrTimeout();
            await connection.DisposeAsync().OrTimeout();
        }

        private bool IsNegotiateRequest(HttpRequestMessage request)
        {
            return request.Method == HttpMethod.Post &&
                new UriBuilder(request.RequestUri).Path.EndsWith("/negotiate");
        }
    }
}
