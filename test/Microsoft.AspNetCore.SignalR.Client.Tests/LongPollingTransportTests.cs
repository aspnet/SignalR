// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http.Connections.Client;
using Microsoft.AspNetCore.Http.Connections.Client.Internal;
using Moq;
using Moq.Protected;
using Xunit;

namespace Microsoft.AspNetCore.SignalR.Client.Tests
{
    public class LongPollingTransportTests
    {
        private static readonly Uri TestUri = new Uri("http://example.com/?id=1234");

        [Fact]
        public async Task LongPollingTransportStopsPollAndSendLoopsWhenTransportStopped()
        {
            var mockHttpHandler = new Mock<HttpMessageHandler>();
            mockHttpHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .Returns<HttpRequestMessage, CancellationToken>(async (request, cancellationToken) =>
                    {
                        await Task.Yield();
                        return ResponseUtils.CreateResponse(HttpStatusCode.OK);
                    });

            Task transportActiveTask;

            using (var httpClient = new HttpClient(mockHttpHandler.Object))
            {
                var longPollingTransport = new LongPollingTransport(httpClient);

                try
                {
                    await longPollingTransport.StartAsync(TestUri, TransferFormat.Binary);

                    transportActiveTask = longPollingTransport.Running;

                    Assert.False(transportActiveTask.IsCompleted);
                }
                finally
                {
                    await longPollingTransport.StopAsync();
                }

                await transportActiveTask.OrTimeout();
            }
        }

        [Fact]
        public async Task LongPollingTransportStopsWhenPollReceives204()
        {
            var mockHttpHandler = new Mock<HttpMessageHandler>();
            mockHttpHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .Returns<HttpRequestMessage, CancellationToken>(async (request, cancellationToken) =>
                {
                    await Task.Yield();
                    return ResponseUtils.CreateResponse(HttpStatusCode.NoContent);
                });

            using (var httpClient = new HttpClient(mockHttpHandler.Object))
            {

                var longPollingTransport = new LongPollingTransport(httpClient);
                try
                {
                    await longPollingTransport.StartAsync(TestUri, TransferFormat.Binary);

                    await longPollingTransport.Running.OrTimeout();

                    Assert.True(longPollingTransport.Input.TryRead(out var result));
                    Assert.True(result.IsCompleted);
                    longPollingTransport.Input.AdvanceTo(result.Buffer.End);
                }
                finally
                {
                    await longPollingTransport.StopAsync();
                }
            }
        }

        [Fact]
        public async Task LongPollingTransportResponseWithNoContentDoesNotStopPoll()
        {
            var requests = 0;
            var mockHttpHandler = new Mock<HttpMessageHandler>();
            mockHttpHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .Returns<HttpRequestMessage, CancellationToken>(async (request, cancellationToken) =>
                {
                    await Task.Yield();

                    if (requests == 0)
                    {
                        requests++;
                        return ResponseUtils.CreateResponse(HttpStatusCode.OK, "Hello");
                    }
                    else if (requests == 1)
                    {
                        requests++;
                        // Time out
                        return ResponseUtils.CreateResponse(HttpStatusCode.OK);
                    }
                    else if (requests == 2)
                    {
                        requests++;
                        return ResponseUtils.CreateResponse(HttpStatusCode.OK, "World");
                    }

                    // Done
                    return ResponseUtils.CreateResponse(HttpStatusCode.NoContent);
                });

            using (var httpClient = new HttpClient(mockHttpHandler.Object))
            {

                var longPollingTransport = new LongPollingTransport(httpClient);
                try
                {
                    await longPollingTransport.StartAsync(TestUri, TransferFormat.Binary);

                    var data = await longPollingTransport.Input.ReadAllAsync().OrTimeout();
                    await longPollingTransport.Running.OrTimeout();
                    Assert.Equal(Encoding.UTF8.GetBytes("HelloWorld"), data);
                }
                finally
                {
                    await longPollingTransport.StopAsync();
                }
            }
        }

        [Fact]
        public async Task LongPollingTransportStopsWhenPollRequestFails()
        {
            var mockHttpHandler = new Mock<HttpMessageHandler>();
            mockHttpHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .Returns<HttpRequestMessage, CancellationToken>(async (request, cancellationToken) =>
                {
                    await Task.Yield();
                    return ResponseUtils.CreateResponse(HttpStatusCode.InternalServerError);
                });

            using (var httpClient = new HttpClient(mockHttpHandler.Object))
            {
                var longPollingTransport = new LongPollingTransport(httpClient);
                try
                {
                    await longPollingTransport.StartAsync(TestUri, TransferFormat.Binary);

                    var exception =
                        await Assert.ThrowsAsync<HttpRequestException>(async () =>
                        {
                            async Task ReadAsync()
                            {
                                await longPollingTransport.Input.ReadAsync();
                            }

                            await ReadAsync().OrTimeout();
                        });
                    Assert.Contains(" 500 ", exception.Message);
                }
                finally
                {
                    await longPollingTransport.StopAsync();
                }
            }
        }

        [Fact]
        public async Task LongPollingTransportStopsWhenSendRequestFails()
        {
            var stopped = false;
            var mockHttpHandler = new Mock<HttpMessageHandler>();
            mockHttpHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .Returns<HttpRequestMessage, CancellationToken>(async (request, cancellationToken) =>
                {
                    await Task.Yield();
                    switch (request.Method.Method)
                    {
                        case "DELETE":
                            stopped = true;
                            return ResponseUtils.CreateResponse(HttpStatusCode.Accepted);
                        case "GET" when stopped:
                            return ResponseUtils.CreateResponse(HttpStatusCode.NoContent);
                        case "GET":
                            return ResponseUtils.CreateResponse(HttpStatusCode.OK);
                        case "POST":
                            return ResponseUtils.CreateResponse(HttpStatusCode.InternalServerError);
                        default:
                            throw new InvalidOperationException("Unexpected request");
                    }
                });

            using (var httpClient = new HttpClient(mockHttpHandler.Object))
            {
                var longPollingTransport = new LongPollingTransport(httpClient);
                try
                {
                    await longPollingTransport.StartAsync(TestUri, TransferFormat.Binary);

                    await longPollingTransport.Output.WriteAsync(Encoding.UTF8.GetBytes("Hello World"));

                    await longPollingTransport.Running.OrTimeout();

                    var exception = await Assert.ThrowsAsync<HttpRequestException>(async () => await longPollingTransport.Input.ReadAllAsync().OrTimeout());
                    Assert.Contains(" 500 ", exception.Message);

                    Assert.True(stopped);
                }
                finally
                {
                    await longPollingTransport.StopAsync();
                }
            }
        }

        [Fact]
        public async Task LongPollingTransportShutsDownWhenChannelIsClosed()
        {
            var mockHttpHandler = new Mock<HttpMessageHandler>();
            var stopped = false;
            mockHttpHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .Returns<HttpRequestMessage, CancellationToken>(async (request, cancellationToken) =>
                {
                    await Task.Yield();
                    if (request.Method == HttpMethod.Delete)
                    {
                        stopped = true;
                        return ResponseUtils.CreateResponse(HttpStatusCode.Accepted);
                    }
                    else
                    {
                        return stopped
                            ? ResponseUtils.CreateResponse(HttpStatusCode.NoContent)
                            : ResponseUtils.CreateResponse(HttpStatusCode.OK);
                    }
                });

            using (var httpClient = new HttpClient(mockHttpHandler.Object))
            {
                var longPollingTransport = new LongPollingTransport(httpClient);
                try
                {
                    await longPollingTransport.StartAsync(TestUri, TransferFormat.Binary);

                    longPollingTransport.Output.Complete();

                    await longPollingTransport.Running.OrTimeout();

                    await longPollingTransport.Input.ReadAllAsync().OrTimeout();
                }
                finally
                {
                    await longPollingTransport.StopAsync();
                }
            }
        }

        [Fact]
        public async Task LongPollingTransportShutsDownAfterTimeoutEvenIfServerDoesntCompletePoll()
        {
            var mockHttpHandler = new Mock<HttpMessageHandler>();
            mockHttpHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .Returns<HttpRequestMessage, CancellationToken>(async (request, cancellationToken) =>
                {
                    await Task.Yield();
                    return ResponseUtils.CreateResponse(HttpStatusCode.OK);
                });

            using (var httpClient = new HttpClient(mockHttpHandler.Object))
            {
                var longPollingTransport = new LongPollingTransport(httpClient);
                longPollingTransport.ShutdownTimeout = TimeSpan.FromMilliseconds(1);

                try
                {
                    await longPollingTransport.StartAsync(TestUri, TransferFormat.Binary);

                    longPollingTransport.Output.Complete();

                    await longPollingTransport.Running.OrTimeout();

                    await longPollingTransport.Input.ReadAllAsync().OrTimeout();
                }
                finally
                {
                    await longPollingTransport.StopAsync();
                }
            }
        }

        [Fact]
        public async Task LongPollingTransportDispatchesMessagesReceivedFromPoll()
        {
            var message1Payload = new[] { (byte)'H', (byte)'e', (byte)'l', (byte)'l', (byte)'o' };

            var firstCall = true;
            var mockHttpHandler = new Mock<HttpMessageHandler>();
            var sentRequests = new List<HttpRequestMessage>();
            mockHttpHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .Returns<HttpRequestMessage, CancellationToken>(async (request, cancellationToken) =>
                {
                    sentRequests.Add(request);

                    await Task.Yield();

                    if (firstCall)
                    {
                        firstCall = false;
                        return ResponseUtils.CreateResponse(HttpStatusCode.OK, message1Payload);
                    }

                    return ResponseUtils.CreateResponse(HttpStatusCode.NoContent);
                });

            using (var httpClient = new HttpClient(mockHttpHandler.Object))
            {
                var longPollingTransport = new LongPollingTransport(httpClient);
                try
                {
                    // Start the transport
                    await longPollingTransport.StartAsync(TestUri, TransferFormat.Binary);

                    // Wait for the transport to finish
                    await longPollingTransport.Running.OrTimeout();

                    // Pull Messages out of the channel
                    var message = await longPollingTransport.Input.ReadAllAsync();

                    // Check the provided request
                    Assert.Equal(2, sentRequests.Count);

                    // Check the messages received
                    Assert.Equal(message1Payload, message);
                }
                finally
                {
                    await longPollingTransport.StopAsync();
                }
            }
        }

        [Fact]
        public async Task LongPollingTransportSendsAvailableMessagesWhenTheyArrive()
        {
            var sentRequests = new List<byte[]>();
            var tcs = new TaskCompletionSource<HttpResponseMessage>();

            var mockHttpHandler = new Mock<HttpMessageHandler>();
            mockHttpHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .Returns<HttpRequestMessage, CancellationToken>(async (request, cancellationToken) =>
                {
                    await Task.Yield();
                    if (request.Method == HttpMethod.Post)
                    {
                        // Build a new request object, but convert the entire payload to string
                        sentRequests.Add(await request.Content.ReadAsByteArrayAsync());
                    }
                    else if (request.Method == HttpMethod.Get)
                    {
                        // This is the poll task
                        return await tcs.Task;
                    }
                    else if (request.Method == HttpMethod.Delete)
                    {
                        tcs.TrySetResult(ResponseUtils.CreateResponse(HttpStatusCode.NoContent));
                    }
                    return ResponseUtils.CreateResponse(HttpStatusCode.OK);
                });

            using (var httpClient = new HttpClient(mockHttpHandler.Object))
            {
                var longPollingTransport = new LongPollingTransport(httpClient);

                try
                {
                    // Start the transport
                    await longPollingTransport.StartAsync(TestUri, TransferFormat.Binary);

                    longPollingTransport.Output.Write(Encoding.UTF8.GetBytes("Hello"));
                    longPollingTransport.Output.Write(Encoding.UTF8.GetBytes("World"));
                    await longPollingTransport.Output.FlushAsync();

                    longPollingTransport.Output.Complete();

                    await longPollingTransport.Running.OrTimeout();
                    await longPollingTransport.Input.ReadAllAsync();

                    Assert.Single(sentRequests);
                    Assert.Equal(new[] { (byte)'H', (byte)'e', (byte)'l', (byte)'l', (byte)'o', (byte)'W', (byte)'o', (byte)'r', (byte)'l', (byte)'d'
                    }, sentRequests[0]);
                }
                finally
                {
                    await longPollingTransport.StopAsync();
                }
            }
        }

        [Theory]
        [InlineData(TransferFormat.Binary)]
        [InlineData(TransferFormat.Text)]
        public async Task LongPollingTransportSetsTransferFormat(TransferFormat transferFormat)
        {
            var mockHttpHandler = new Mock<HttpMessageHandler>();
            mockHttpHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .Returns<HttpRequestMessage, CancellationToken>(async (request, cancellationToken) =>
                {
                    await Task.Yield();
                    return ResponseUtils.CreateResponse(HttpStatusCode.OK);
                });

            using (var httpClient = new HttpClient(mockHttpHandler.Object))
            {
                var longPollingTransport = new LongPollingTransport(httpClient);

                try
                {
                    await longPollingTransport.StartAsync(TestUri, transferFormat);
                }
                finally
                {
                    await longPollingTransport.StopAsync();
                }
            }
        }

        [Theory]
        [InlineData(TransferFormat.Text | TransferFormat.Binary)] // Multiple values not allowed
        [InlineData((TransferFormat)42)] // Unexpected value
        public async Task LongPollingTransportThrowsForInvalidTransferFormat(TransferFormat transferFormat)
        {
            var mockHttpHandler = new Mock<HttpMessageHandler>();
            mockHttpHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .Returns<HttpRequestMessage, CancellationToken>(async (request, cancellationToken) =>
                {
                    await Task.Yield();
                    return ResponseUtils.CreateResponse(HttpStatusCode.OK);
                });

            using (var httpClient = new HttpClient(mockHttpHandler.Object))
            {
                var longPollingTransport = new LongPollingTransport(httpClient);
                var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
                    longPollingTransport.StartAsync(TestUri, transferFormat));

                Assert.Contains($"The '{transferFormat}' transfer format is not supported by this transport.", exception.Message);
                Assert.Equal("transferFormat", exception.ParamName);
            }
        }

        [Fact]
        public async Task LongPollingTransportRePollsIfRequestCancelled()
        {
            var numPolls = 0;
            var completionTcs = new TaskCompletionSource<object>();

            var mockHttpHandler = new Mock<HttpMessageHandler>();
            mockHttpHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .Returns<HttpRequestMessage, CancellationToken>(async (request, cancellationToken) =>
                {
                    await Task.Yield();

                    if (Interlocked.Increment(ref numPolls) < 3)
                    {
                        throw new OperationCanceledException();
                    }

                    completionTcs.SetResult(null);
                    return ResponseUtils.CreateResponse(HttpStatusCode.OK);
                });

            using (var httpClient = new HttpClient(mockHttpHandler.Object))
            {
                var longPollingTransport = new LongPollingTransport(httpClient);

                try
                {
                    await longPollingTransport.StartAsync(TestUri, TransferFormat.Binary);

                    var completedTask = await Task.WhenAny(completionTcs.Task, longPollingTransport.Running).OrTimeout();
                    Assert.Equal(completionTcs.Task, completedTask);
                }
                finally
                {
                    await longPollingTransport.StopAsync();
                }
            }
        }

        [Fact]
        public async Task SendsDeleteRequestWhenTransportCompleted()
        {
            var handler = TestHttpMessageHandler.CreateDefault();

            using (var httpClient = new HttpClient(handler))
            {
                var longPollingTransport = new LongPollingTransport(httpClient);

                await longPollingTransport.StartAsync(TestUri, TransferFormat.Binary);
                await longPollingTransport.StopAsync();

                var deleteRequest = handler.ReceivedRequests.SingleOrDefault(r => r.Method == HttpMethod.Delete);
                Assert.NotNull(deleteRequest);
                Assert.Equal(TestUri, deleteRequest.RequestUri);
            }
        }
    }
}
