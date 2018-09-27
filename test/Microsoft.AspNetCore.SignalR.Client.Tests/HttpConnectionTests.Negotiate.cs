// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Http.Connections.Client;
using Microsoft.AspNetCore.Http.Connections.Client.Internal;
using Microsoft.AspNetCore.SignalR.Tests;
using Microsoft.Extensions.Logging.Testing;
using Moq;
using Newtonsoft.Json;
using Xunit;

namespace Microsoft.AspNetCore.SignalR.Client.Tests
{
    public partial class HttpConnectionTests
    {
        public class Negotiate
        {
            [Theory]
            [InlineData("")]
            [InlineData("Not Json")]
            public Task StartThrowsFormatExceptionIfNegotiationResponseIsInvalid(string negotiatePayload)
            {
                return RunInvalidNegotiateResponseTest<InvalidDataException>(negotiatePayload, "Invalid negotiation response received.");
            }

            [Fact]
            public Task StartThrowsFormatExceptionIfNegotiationResponseHasNoConnectionId()
            {
                return RunInvalidNegotiateResponseTest<FormatException>(ResponseUtils.CreateNegotiationContent(connectionId: string.Empty), "Invalid connection id.");
            }

            [Fact]
            public Task StartThrowsFormatExceptionIfNegotiationResponseHasNoTransports()
            {
                return RunInvalidNegotiateResponseTest<InvalidOperationException>(ResponseUtils.CreateNegotiationContent(transportTypes: 0), "Unable to connect to the server with any of the available transports.");
            }

            [Theory]
            [InlineData(HttpTransportType.None)]
            [InlineData(HttpTransportType.ServerSentEvents)]
            public Task ConnectionCannotBeStartedIfNoCommonTransportsBetweenClientAndServer(HttpTransportType serverTransports)
            {
                return RunInvalidNegotiateResponseTest<InvalidOperationException>(ResponseUtils.CreateNegotiationContent(transportTypes: serverTransports), "Unable to connect to the server with any of the available transports.");
            }

            [Theory]
            [InlineData("http://fakeuri.org/", "http://fakeuri.org/negotiate")]
            [InlineData("http://fakeuri.org/?q=1/0", "http://fakeuri.org/negotiate?q=1/0")]
            [InlineData("http://fakeuri.org?q=1/0", "http://fakeuri.org/negotiate?q=1/0")]
            [InlineData("http://fakeuri.org/endpoint", "http://fakeuri.org/endpoint/negotiate")]
            [InlineData("http://fakeuri.org/endpoint/", "http://fakeuri.org/endpoint/negotiate")]
            [InlineData("http://fakeuri.org/endpoint?q=1/0", "http://fakeuri.org/endpoint/negotiate?q=1/0")]
            public async Task CorrectlyHandlesQueryStringWhenAppendingNegotiateToUrl(string requestedUrl, string expectedNegotiate)
            {
                var testHttpHandler = new TestHttpMessageHandler(autoNegotiate: false);

                var negotiateUrlTcs = new TaskCompletionSource<string>();
                testHttpHandler.OnLongPoll(cancellationToken => ResponseUtils.CreateResponse(HttpStatusCode.NoContent));
                testHttpHandler.OnLongPollDelete(cancellationToken => ResponseUtils.CreateResponse(HttpStatusCode.NoContent));
                testHttpHandler.OnNegotiate((request, cancellationToken) =>
                {
                    negotiateUrlTcs.TrySetResult(request.RequestUri.ToString());
                    return ResponseUtils.CreateResponse(HttpStatusCode.OK,
                        ResponseUtils.CreateNegotiationContent());
                });

                using (var noErrorScope = new VerifyNoErrorsScope())
                {
                    await WithConnectionAsync(
                        CreateConnection(testHttpHandler, url: requestedUrl, loggerFactory: noErrorScope.LoggerFactory),
                        async (connection) =>
                        {
                            await connection.StartAsync(TransferFormat.Text).OrTimeout();
                        });
                }

                Assert.Equal(expectedNegotiate, await negotiateUrlTcs.Task.OrTimeout());
            }

            [Fact]
            public async Task NegotiateReturnedConnectionIdIsSetOnConnection()
            {
                string connectionId = null;

                var testHttpHandler = new TestHttpMessageHandler(autoNegotiate: false);
                testHttpHandler.OnNegotiate((request, cancellationToken) => ResponseUtils.CreateResponse(HttpStatusCode.OK,
                    JsonConvert.SerializeObject(new
                    {
                        connectionId = "0rge0d00-0040-0030-0r00-000q00r00e00",
                        availableTransports = new object[]
                        {
                            new
                            {
                                transport = "LongPolling",
                                transferFormats = new[] { "Text" }
                            },
                        }
                    })));
                testHttpHandler.OnLongPoll(cancellationToken => ResponseUtils.CreateResponse(HttpStatusCode.NoContent));
                testHttpHandler.OnLongPollDelete((token) => ResponseUtils.CreateResponse(HttpStatusCode.Accepted));

                using (var noErrorScope = new VerifyNoErrorsScope())
                {
                    await WithConnectionAsync(
                        CreateConnection(testHttpHandler, loggerFactory: noErrorScope.LoggerFactory),
                        async (connection) =>
                        {
                            await connection.StartAsync(TransferFormat.Text).OrTimeout();
                            connectionId = connection.ConnectionId;
                        });
                }

                Assert.Equal("0rge0d00-0040-0030-0r00-000q00r00e00", connectionId);
            }

            [Fact]
            public async Task NegotiateThatReturnsUrlGetFollowed()
            {
                var testHttpHandler = new TestHttpMessageHandler(autoNegotiate: false);
                var firstNegotiate = true;
                testHttpHandler.OnNegotiate((request, cancellationToken) =>
                {
                    if (firstNegotiate)
                    {
                        firstNegotiate = false;
                        return ResponseUtils.CreateResponse(HttpStatusCode.OK,
                            JsonConvert.SerializeObject(new
                            {
                                url = "https://another.domain.url/chat"
                            }));
                    }

                    return ResponseUtils.CreateResponse(HttpStatusCode.OK,
                        JsonConvert.SerializeObject(new
                        {
                            connectionId = "0rge0d00-0040-0030-0r00-000q00r00e00",
                            availableTransports = new object[]
                            {
                                new
                                {
                                    transport = "LongPolling",
                                    transferFormats = new[] { "Text" }
                                },
                            }
                        }));
                });

                testHttpHandler.OnLongPoll((token) =>
                {
                    var tcs = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);

                    token.Register(() => tcs.TrySetResult(ResponseUtils.CreateResponse(HttpStatusCode.NoContent)));

                    return tcs.Task;
                });

                testHttpHandler.OnLongPollDelete((token) => ResponseUtils.CreateResponse(HttpStatusCode.Accepted));

                using (var noErrorScope = new VerifyNoErrorsScope())
                {
                    await WithConnectionAsync(
                        CreateConnection(testHttpHandler, loggerFactory: noErrorScope.LoggerFactory),
                        async (connection) =>
                        {
                            await connection.StartAsync(TransferFormat.Text).OrTimeout();
                        });
                }

                Assert.Equal("http://fakeuri.org/negotiate", testHttpHandler.ReceivedRequests[0].RequestUri.ToString());
                Assert.Equal("https://another.domain.url/chat/negotiate", testHttpHandler.ReceivedRequests[1].RequestUri.ToString());
                Assert.Equal("https://another.domain.url/chat?id=0rge0d00-0040-0030-0r00-000q00r00e00", testHttpHandler.ReceivedRequests[2].RequestUri.ToString());
                Assert.Equal("https://another.domain.url/chat?id=0rge0d00-0040-0030-0r00-000q00r00e00", testHttpHandler.ReceivedRequests[3].RequestUri.ToString());
                Assert.Equal(5, testHttpHandler.ReceivedRequests.Count);
            }

            [Fact]
            public async Task NegotiateThatReturnsRedirectUrlForeverThrowsAfter100Tries()
            {
                var testHttpHandler = new TestHttpMessageHandler(autoNegotiate: false);
                testHttpHandler.OnNegotiate((request, cancellationToken) =>
                {
                    return ResponseUtils.CreateResponse(HttpStatusCode.OK,
                            JsonConvert.SerializeObject(new
                            {
                                url = "https://another.domain.url/chat"
                            }));
                });

                using (var noErrorScope = new VerifyNoErrorsScope())
                {
                    await WithConnectionAsync(
                        CreateConnection(testHttpHandler, loggerFactory: noErrorScope.LoggerFactory),
                        async (connection) =>
                        {
                            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => connection.StartAsync(TransferFormat.Text).OrTimeout());
                            Assert.Equal("Negotiate redirection limit exceeded.", exception.Message);
                        });
                }
            }

            [Fact]
            public async Task NegotiateThatReturnsUrlGetFollowedWithAccessToken()
            {
                var testHttpHandler = new TestHttpMessageHandler(autoNegotiate: false);
                var firstNegotiate = true;
                testHttpHandler.OnNegotiate((request, cancellationToken) =>
                {
                    if (firstNegotiate)
                    {
                        firstNegotiate = false;

                        // The first negotiate requires an access token
                        if (request.Headers.Authorization?.Parameter != "firstSecret")
                        {
                            return ResponseUtils.CreateResponse(HttpStatusCode.Unauthorized);
                        }

                        return ResponseUtils.CreateResponse(HttpStatusCode.OK,
                            JsonConvert.SerializeObject(new
                            {
                                url = "https://another.domain.url/chat",
                                accessToken = "secondSecret"
                            }));
                    }

                    // All other requests require an access token
                    if (request.Headers.Authorization?.Parameter != "secondSecret")
                    {
                        return ResponseUtils.CreateResponse(HttpStatusCode.Unauthorized);
                    }

                    return ResponseUtils.CreateResponse(HttpStatusCode.OK,
                        JsonConvert.SerializeObject(new
                        {
                            connectionId = "0rge0d00-0040-0030-0r00-000q00r00e00",
                            availableTransports = new object[]
                            {
                                new
                                {
                                    transport = "LongPolling",
                                    transferFormats = new[] { "Text" }
                                },
                            }
                        }));
                });

                testHttpHandler.OnLongPoll((request, token) =>
                {
                    // All other requests require an access token
                    if (request.Headers.Authorization?.Parameter != "secondSecret")
                    {
                        return Task.FromResult(ResponseUtils.CreateResponse(HttpStatusCode.Unauthorized));
                    }
                    var tcs = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);

                    token.Register(() => tcs.TrySetResult(ResponseUtils.CreateResponse(HttpStatusCode.NoContent)));

                    return tcs.Task;
                });

                testHttpHandler.OnLongPollDelete((token) => ResponseUtils.CreateResponse(HttpStatusCode.Accepted));

                Task<string> AccessTokenProvider() => Task.FromResult<string>("firstSecret");

                using (var noErrorScope = new VerifyNoErrorsScope())
                {
                    await WithConnectionAsync(
                        CreateConnection(testHttpHandler, loggerFactory: noErrorScope.LoggerFactory, accessTokenProvider: AccessTokenProvider),
                        async (connection) =>
                        {
                            await connection.StartAsync(TransferFormat.Text).OrTimeout();
                        });
                }

                Assert.Equal("http://fakeuri.org/negotiate", testHttpHandler.ReceivedRequests[0].RequestUri.ToString());
                Assert.Equal("https://another.domain.url/chat/negotiate", testHttpHandler.ReceivedRequests[1].RequestUri.ToString());
                Assert.Equal("https://another.domain.url/chat?id=0rge0d00-0040-0030-0r00-000q00r00e00", testHttpHandler.ReceivedRequests[2].RequestUri.ToString());
                Assert.Equal("https://another.domain.url/chat?id=0rge0d00-0040-0030-0r00-000q00r00e00", testHttpHandler.ReceivedRequests[3].RequestUri.ToString());
                // Delete request
                Assert.Equal(5, testHttpHandler.ReceivedRequests.Count);
            }

            [Fact]
            public async Task StartSkipsOverTransportsThatTheClientDoesNotUnderstand()
            {
                var testHttpHandler = new TestHttpMessageHandler(autoNegotiate: false);

                testHttpHandler.OnLongPoll(cancellationToken => ResponseUtils.CreateResponse(HttpStatusCode.NoContent));
                testHttpHandler.OnNegotiate((request, cancellationToken) =>
                {
                    return ResponseUtils.CreateResponse(HttpStatusCode.OK,
                        JsonConvert.SerializeObject(new
                        {
                            connectionId = "00000000-0000-0000-0000-000000000000",
                            availableTransports = new object[]
                            {
                                new
                                {
                                    transport = "QuantumEntanglement",
                                    transferFormats = new[] { "Qbits" },
                                },
                                new
                                {
                                    transport = "CarrierPigeon",
                                    transferFormats = new[] { "Text" },
                                },
                                new
                                {
                                    transport = "LongPolling",
                                    transferFormats = new[] { "Text", "Binary" }
                                },
                            }
                        }));
                });

                var transportFactory = new Mock<ITransportFactory>(MockBehavior.Strict);

                transportFactory.Setup(t => t.CreateTransport(HttpTransportType.LongPolling))
                    .Returns(new TestTransport(transferFormat: TransferFormat.Text | TransferFormat.Binary));

                using (var noErrorScope = new VerifyNoErrorsScope())
                {
                    await WithConnectionAsync(
                        CreateConnection(testHttpHandler, transportFactory: transportFactory.Object, loggerFactory: noErrorScope.LoggerFactory),
                        async (connection) =>
                        {
                            await connection.StartAsync(TransferFormat.Binary).OrTimeout();
                        });
                }
            }

            [Fact]
            public async Task StartSkipsOverTransportsThatDoNotSupportTheRequredTransferFormat()
            {
                var testHttpHandler = new TestHttpMessageHandler(autoNegotiate: false);

                testHttpHandler.OnLongPoll(cancellationToken => ResponseUtils.CreateResponse(HttpStatusCode.NoContent));
                testHttpHandler.OnNegotiate((request, cancellationToken) =>
                {
                    return ResponseUtils.CreateResponse(HttpStatusCode.OK,
                        JsonConvert.SerializeObject(new
                        {
                            connectionId = "00000000-0000-0000-0000-000000000000",
                            availableTransports = new object[]
                            {
                                new
                                {
                                    transport = "WebSockets",
                                    transferFormats = new[] { "Qbits" },
                                },
                                new
                                {
                                    transport = "ServerSentEvents",
                                    transferFormats = new[] { "Text" },
                                },
                                new
                                {
                                    transport = "LongPolling",
                                    transferFormats = new[] { "Text", "Binary" }
                                },
                            }
                        }));
                });

                var transportFactory = new Mock<ITransportFactory>(MockBehavior.Strict);

                transportFactory.Setup(t => t.CreateTransport(HttpTransportType.LongPolling))
                    .Returns(new TestTransport(transferFormat: TransferFormat.Text | TransferFormat.Binary));

                await WithConnectionAsync(
                    CreateConnection(testHttpHandler, transportFactory: transportFactory.Object),
                    async (connection) =>
                    {
                        await connection.StartAsync(TransferFormat.Binary).OrTimeout();
                    });
            }

            [Fact]
            public async Task NegotiateThatReturnsErrorThrowsFromStart()
            {
                bool ExpectedError(WriteContext writeContext)
                {
                    return writeContext.LoggerName == typeof(HttpConnection).FullName &&
                        writeContext.EventId.Name == "ErrorWithNegotiation";
                }

                var testHttpHandler = new TestHttpMessageHandler(autoNegotiate: false);
                testHttpHandler.OnNegotiate((request, cancellationToken) =>
                {
                    return ResponseUtils.CreateResponse(HttpStatusCode.OK,
                            JsonConvert.SerializeObject(new
                            {
                                error = "Test error."
                            }));
                });

                using (var noErrorScope = new VerifyNoErrorsScope(expectedErrorsFilter: ExpectedError))
                {
                    await WithConnectionAsync(
                        CreateConnection(testHttpHandler, loggerFactory: noErrorScope.LoggerFactory),
                        async (connection) =>
                        {
                            var exception = await Assert.ThrowsAsync<Exception>(() => connection.StartAsync(TransferFormat.Text).OrTimeout());
                            Assert.Equal("Test error.", exception.Message);
                        });
                }
            }

            private async Task RunInvalidNegotiateResponseTest<TException>(string negotiatePayload, string expectedExceptionMessage) where TException : Exception
            {
                var testHttpHandler = new TestHttpMessageHandler(autoNegotiate: false);

                testHttpHandler.OnNegotiate((_, cancellationToken) => ResponseUtils.CreateResponse(HttpStatusCode.OK, negotiatePayload));

                await WithConnectionAsync(
                    CreateConnection(testHttpHandler),
                    async (connection) =>
                    {
                        var exception = await Assert.ThrowsAsync<TException>(
                            () => connection.StartAsync(TransferFormat.Text).OrTimeout());

                        Assert.Equal(expectedExceptionMessage, exception.Message);
                    });
            }
        }
    }
}
