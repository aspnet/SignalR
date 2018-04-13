// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO.Pipelines;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Http.Connections.Client;
using Microsoft.AspNetCore.Http.Connections.Client.Internal;
using Xunit;

namespace Microsoft.AspNetCore.SignalR.Client.Tests
{
    public partial class HttpConnectionTests
    {
        public class Transport
        {
            [Theory]
            [InlineData(HttpTransportType.LongPolling)]
            [InlineData(HttpTransportType.ServerSentEvents)]
            public async Task HttpConnectionSetsAccessTokenOnAllRequests(HttpTransportType transportType)
            {
                var testHttpHandler = new TestHttpMessageHandler(autoNegotiate: false);
                var requestsExecuted = false;
                var callCount = 0;

                testHttpHandler.OnRequest((request, next, token) =>
                {
                    return Task.FromResult(ResponseUtils.CreateResponse(HttpStatusCode.NoContent));
                });

                testHttpHandler.OnNegotiate((_, cancellationToken) =>
                {
                    return ResponseUtils.CreateResponse(HttpStatusCode.OK, ResponseUtils.CreateNegotiationContent());
                });

                testHttpHandler.OnRequest(async (request, next, token) =>
                {
                    Assert.Equal("Bearer", request.Headers.Authorization.Scheme);

                    // Call count increments with each call and is used as the access token
                    Assert.Equal(callCount.ToString(), request.Headers.Authorization.Parameter);

                    requestsExecuted = true;

                    return await next();
                });

                Task<string> AccessTokenFactory()
                {
                    callCount++;
                    return Task.FromResult(callCount.ToString());
                }

                await WithConnectionAsync(
                    CreateConnection(testHttpHandler, transportType: transportType, accessTokenFactory: AccessTokenFactory),
                    async (connection) =>
                    {
                        await connection.StartAsync(TransferFormat.Text).OrTimeout();
                        await connection.Transport.Output.WriteAsync(Encoding.UTF8.GetBytes("Hello world 1"));
                        await connection.Transport.Output.WriteAsync(Encoding.UTF8.GetBytes("Hello world 2"));
                    });
                // Fail safe in case the code is modified and some requests don't execute as a result
                Assert.True(requestsExecuted);
            }

            [Theory]
            [InlineData(HttpTransportType.LongPolling)]
            [InlineData(HttpTransportType.ServerSentEvents)]
            public async Task HttpConnectionSetsUserAgentOnAllRequests(HttpTransportType transportType)
            {
                var testHttpHandler = new TestHttpMessageHandler(autoNegotiate: false);
                var requestsExecuted = false;

                testHttpHandler.OnRequest((request, next, token) =>
                {
                    return Task.FromResult(ResponseUtils.CreateResponse(HttpStatusCode.NoContent));
                });

                testHttpHandler.OnNegotiate((_, cancellationToken) =>
                {
                    return ResponseUtils.CreateResponse(HttpStatusCode.OK, ResponseUtils.CreateNegotiationContent());
                });

                testHttpHandler.OnRequest(async (request, next, token) =>
                {
                    var userAgentHeaderCollection = request.Headers.UserAgent;
                    var userAgentHeader = Assert.Single(userAgentHeaderCollection);
                    Assert.Equal("Microsoft.AspNetCore.Http.Connections.Client", userAgentHeader.Product.Name);

                    // user agent version should come from version embedded in assembly metadata
                    var assemblyVersion = typeof(Constants)
                            .Assembly
                            .GetCustomAttribute<AssemblyInformationalVersionAttribute>();

                    Assert.Equal(assemblyVersion.InformationalVersion, userAgentHeader.Product.Version);

                    requestsExecuted = true;

                    return await next();
                });

                await WithConnectionAsync(
                    CreateConnection(testHttpHandler, transportType: transportType),
                    async (connection) =>
                    {
                        await connection.StartAsync(TransferFormat.Text).OrTimeout();
                        await connection.Transport.Output.WriteAsync(Encoding.UTF8.GetBytes("Hello World"));
                    });
                // Fail safe in case the code is modified and some requests don't execute as a result
                Assert.True(requestsExecuted);
            }

            [Theory]
            [InlineData(HttpTransportType.LongPolling)]
            [InlineData(HttpTransportType.ServerSentEvents)]
            public async Task HttpConnectionSetsRequestedWithOnAllRequests(HttpTransportType transportType)
            {
                var testHttpHandler = new TestHttpMessageHandler(autoNegotiate: false);
                var requestsExecuted = false;

                testHttpHandler.OnRequest((request, next, token) =>
                {
                    return Task.FromResult(ResponseUtils.CreateResponse(HttpStatusCode.NoContent));
                });

                testHttpHandler.OnNegotiate((_, cancellationToken) =>
                {
                    return ResponseUtils.CreateResponse(HttpStatusCode.OK, ResponseUtils.CreateNegotiationContent());
                });

                testHttpHandler.OnRequest(async (request, next, token) =>
                {
                    var requestedWithHeader = request.Headers.GetValues("X-Requested-With");
                    var requestedWithValue = Assert.Single(requestedWithHeader);
                    Assert.Equal("XMLHttpRequest", requestedWithValue);

                    requestsExecuted = true;

                    return await next();
                });

                await WithConnectionAsync(
                    CreateConnection(testHttpHandler, transportType: transportType),
                    async (connection) =>
                    {
                        await connection.StartAsync(TransferFormat.Text).OrTimeout();
                        await connection.Transport.Output.WriteAsync(Encoding.UTF8.GetBytes("Hello World"));
                    });
                // Fail safe in case the code is modified and some requests don't execute as a result
                Assert.True(requestsExecuted);
            }

            [Fact]
            public async Task CanReceiveData()
            {
                var testHttpHandler = new TestHttpMessageHandler();

                // Set the long poll up to return a single message over a few polls.
                var requestCount = 0;
                var messageFragments = new[] { "This ", "is ", "a ", "test" };
                testHttpHandler.OnLongPoll(cancellationToken =>
                {
                    if (requestCount >= messageFragments.Length)
                    {
                        return ResponseUtils.CreateResponse(HttpStatusCode.NoContent);
                    }

                    var resp = ResponseUtils.CreateResponse(HttpStatusCode.OK, messageFragments[requestCount]);
                    requestCount += 1;
                    return resp;
                });
                testHttpHandler.OnSocketSend((_, __) => ResponseUtils.CreateResponse(HttpStatusCode.Accepted));

                await WithConnectionAsync(
                    CreateConnection(testHttpHandler),
                    async (connection) =>
                    {
                        await connection.StartAsync(TransferFormat.Text).OrTimeout();
                        Assert.Contains("This is a test", Encoding.UTF8.GetString(await connection.Transport.Input.ReadAllAsync()));
                    });
            }

            [Fact]
            public async Task CanSendData()
            {
                var data = new byte[] { 1, 1, 2, 3, 5, 8 };

                var testHttpHandler = new TestHttpMessageHandler();

                var sendTcs = new TaskCompletionSource<byte[]>();
                var longPollTcs = new TaskCompletionSource<HttpResponseMessage>();

                testHttpHandler.OnLongPoll(cancellationToken => longPollTcs.Task);

                testHttpHandler.OnSocketSend((buf, cancellationToken) =>
                {
                    sendTcs.TrySetResult(buf);
                    return Task.FromResult(ResponseUtils.CreateResponse(HttpStatusCode.Accepted));
                });

                await WithConnectionAsync(
                    CreateConnection(testHttpHandler),
                    async (connection) =>
                    {
                        await connection.StartAsync(TransferFormat.Text).OrTimeout();

                        await connection.Transport.Output.WriteAsync(data).OrTimeout();

                        Assert.Equal(data, await sendTcs.Task.OrTimeout());

                        longPollTcs.TrySetResult(ResponseUtils.CreateResponse(HttpStatusCode.NoContent));
                    });
            }

            [Fact]
            public Task SendThrowsIfConnectionIsNotStarted()
            {
                return WithConnectionAsync(
                    CreateConnection(),
                    async (connection) =>
                    {
                        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                            () => connection.Transport.Output.WriteAsync(new byte[0]).OrTimeout());
                        Assert.Equal($"Cannot access the {nameof(Transport)} pipe before the connection has started.", exception.Message);
                    });
            }

            [Fact]
            public Task TransportPipeCannotBeAccessedAfterConnectionIsDisposed()
            {
                return WithConnectionAsync(
                    CreateConnection(),
                    async (connection) =>
                    {
                        await connection.StartAsync(TransferFormat.Text).OrTimeout();
                        await connection.DisposeAsync().OrTimeout();

                        var exception = await Assert.ThrowsAsync<ObjectDisposedException>(
                            () => connection.Transport.Output.WriteAsync(new byte[0]).OrTimeout());
                        Assert.Equal(nameof(HttpConnection), exception.ObjectName);
                    });
            }

            [Fact]
            public Task TransportIsShutDownAfterDispose()
            {
                var transport = new TestTransport();
                return WithConnectionAsync(
                    CreateConnection(transport: transport),
                    async (connection) =>
                    {
                        await connection.StartAsync(TransferFormat.Text).OrTimeout();
                        await connection.DisposeAsync().OrTimeout();

                        // This will throw OperationCancelledException if it's forcibly terminated
                        // which we don't want
                        await transport.Receiving.OrTimeout();
                    });
            }
        }
    }
}
