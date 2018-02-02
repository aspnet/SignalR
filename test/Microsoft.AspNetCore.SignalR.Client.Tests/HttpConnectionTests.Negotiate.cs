// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Client.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Xunit;
using Xunit.Abstractions;
using TransportType = Microsoft.AspNetCore.Sockets.TransportType;

namespace Microsoft.AspNetCore.SignalR.Client.Tests
{
    public partial class HttpConnectionTests
    {
        public class Negotiate : LoggedTest
        {
            public Negotiate(ITestOutputHelper output) : base(output)
            {
            }

            [Theory]
            [InlineData("")]
            [InlineData("Not Json")]
            public Task StartThrowsFormatExceptionIfNegotiationResponseIsInvalid(string negotiatePayload)
            {
                using (StartLog(out var loggerFactory))
                {
                    return RunInvalidNegotiateResponseTest<FormatException>(negotiatePayload, "Invalid negotiation response received.", loggerFactory);
                }
            }

            [Fact]
            public Task StartThrowsFormatExceptionIfNegotiationResponseHasNoConnectionId()
            {
                using (StartLog(out var loggerFactory))
                {
                    return RunInvalidNegotiateResponseTest<FormatException>(ResponseUtils.CreateNegotiationContent(connectionId: null), "Invalid connection id returned in negotiation response.", loggerFactory);
                }
            }

            [Fact]
            public Task StartThrowsFormatExceptionIfNegotiationResponseHasNoTransports()
            {
                using (StartLog(out var loggerFactory))
                {
                    return RunInvalidNegotiateResponseTest<FormatException>(ResponseUtils.CreateNegotiationContent(transportTypes: null), "No transports returned in negotiation response.", loggerFactory);
                }
            }

            [Theory]
            [InlineData((TransportType)0)]
            [InlineData(TransportType.ServerSentEvents)]
            public Task ConnectionCannotBeStartedIfNoCommonTransportsBetweenClientAndServer(TransportType serverTransports)
            {
                using (StartLog(out var loggerFactory))
                {
                    return RunInvalidNegotiateResponseTest<InvalidOperationException>(ResponseUtils.CreateNegotiationContent(transportTypes: serverTransports), "No requested transports available on the server.", loggerFactory);
                }
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
                using (StartLog(out var loggerFactory, $"{nameof(CorrectlyHandlesQueryStringWhenAppendingNegotiateToUrl)}_Length_{requestedUrl.Length}"))
                {
                    var logger = loggerFactory.CreateLogger<Negotiate>();
                    var testHttpHandler = new TestHttpMessageHandler(autoNegotiate: false, loggerFactory.CreateLogger<TestHttpMessageHandler>());

                    var negotiateUrlTcs = new TaskCompletionSource<string>();
                    testHttpHandler.OnLongPoll(cancellationToken => ResponseUtils.CreateResponse(HttpStatusCode.NoContent));
                    testHttpHandler.OnNegotiate((request, cancellationToken) =>
                    {
                        logger.LogInformation("Received negotiate request at URL: {Url}", request.RequestUri);
                        negotiateUrlTcs.TrySetResult(request.RequestUri.ToString());
                        return ResponseUtils.CreateResponse(HttpStatusCode.OK,
                            ResponseUtils.CreateNegotiationContent());
                    });

                    await WithConnectionAsync(
                        CreateConnection(testHttpHandler, url: requestedUrl, loggerFactory: loggerFactory),
                        async (connection, closed) =>
                        {
                            logger.LogInformation("Starting connection");
                            await connection.StartAsync().OrTimeout();
                            logger.LogInformation("Connection started");
                        });

                    Assert.Equal(expectedNegotiate, await negotiateUrlTcs.Task.OrTimeout());
                }
            }

            private async Task RunInvalidNegotiateResponseTest<TException>(string negotiatePayload, string expectedExceptionMessage, ILoggerFactory loggerFactory) where TException : Exception
            {
                var testHttpHandler = new TestHttpMessageHandler(autoNegotiate: false, loggerFactory.CreateLogger<TestHttpMessageHandler>());
                var logger = loggerFactory.CreateLogger<Negotiate>();

                testHttpHandler.OnNegotiate((_, cancellationToken) => ResponseUtils.CreateResponse(HttpStatusCode.OK, negotiatePayload));

                await WithConnectionAsync(
                    CreateConnection(testHttpHandler),
                    async (connection, closed) =>
                    {
                        logger.LogInformation("Starting connection");
                        var exception = await Assert.ThrowsAsync<TException>(
                            () => connection.StartAsync().OrTimeout());
                        logger.LogInformation("Connection started");

                        Assert.Equal(expectedExceptionMessage, exception.Message);
                    });
            }
        }
    }
}
