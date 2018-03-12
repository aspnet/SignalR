// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Internal;
using Microsoft.AspNetCore.Protocols;
using Microsoft.AspNetCore.Sockets.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Primitives;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.AspNetCore.Sockets.Tests
{
    public class HttpConnectionDispatcherTests : LoggedTest
    {
        public HttpConnectionDispatcherTests(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public async Task NegotiateReservesConnectionIdAndReturnsIt()
        {
            using (StartLog(out var loggerFactory, LogLevel.Debug))
            {
                var manager = CreateConnectionManager(loggerFactory);
                var dispatcher = new HttpConnectionDispatcher(manager, loggerFactory);
                var context = new DefaultHttpContext();
                var services = new ServiceCollection();
                services.AddEndPoint<TestEndPoint>();
                services.AddOptions();
                var ms = new MemoryStream();
                context.Request.Path = "/foo";
                context.Request.Method = "POST";
                context.Response.Body = ms;
                await dispatcher.ExecuteNegotiateAsync(context, new HttpSocketOptions());
                var negotiateResponse = JsonConvert.DeserializeObject<JObject>(Encoding.UTF8.GetString(ms.ToArray()));
                var connectionId = negotiateResponse.Value<string>("connectionId");
                Assert.True(manager.TryGetConnection(connectionId, out var connectionContext));
                Assert.Equal(connectionId, connectionContext.ConnectionId);
            }
        }

        [Fact]
        public async Task CheckThatThresholdValuesAreEnforced()
        {
            using (StartLog(out var loggerFactory, LogLevel.Debug))
            {
                var manager = CreateConnectionManager(loggerFactory);
                var dispatcher = new HttpConnectionDispatcher(manager, loggerFactory);
                var context = new DefaultHttpContext();
                var services = new ServiceCollection();
                services.AddEndPoint<TestEndPoint>();
                services.AddOptions();
                var ms = new MemoryStream();
                context.Request.Path = "/foo";
                context.Request.Method = "POST";
                context.Response.Body = ms;
                var httpSocketOptions = new HttpSocketOptions { TransportMaxBufferSize = 4, ApplicationMaxBufferSize = 4 };
                await dispatcher.ExecuteNegotiateAsync(context, httpSocketOptions);
                var negotiateResponse = JsonConvert.DeserializeObject<JObject>(Encoding.UTF8.GetString(ms.ToArray()));
                var connectionId = negotiateResponse.Value<string>("connectionId");
                Assert.True(manager.TryGetConnection(connectionId, out var connection));

                // This write should complete immediately but it exceeds the writer threshold
                var writeTask = connection.Application.Output.WriteAsync(new byte[] { (byte)'b', (byte)'y', (byte)'t', (byte)'e', (byte)'s' });

                Assert.False(writeTask.IsCompleted);

                // Reading here puts us below the threshold
                await connection.Transport.Input.ConsumeAsync(5);

                await writeTask.AsTask().OrTimeout();
            }
        }

        [Theory]
        [InlineData(TransportType.LongPolling)]
        [InlineData(TransportType.ServerSentEvents)]
        public async Task CheckThatThresholdValuesAreEnforcedWithSends(TransportType transportType)
        {
            using (StartLog(out var loggerFactory, LogLevel.Debug))
            {
                var manager = CreateConnectionManager(loggerFactory);
                var dispatcher = new HttpConnectionDispatcher(manager, loggerFactory);
                var pipeOptions = new PipeOptions(pauseWriterThreshold: 8, resumeWriterThreshold: 4);
                var connection = manager.CreateConnection(pipeOptions, pipeOptions);
                connection.Metadata[ConnectionMetadataNames.Transport] = transportType;

                using (var requestBody = new MemoryStream())
                using (var responseBody = new MemoryStream())
                {
                    var bytes = Encoding.UTF8.GetBytes("EXTRADATA Hi");
                    requestBody.Write(bytes, 0, bytes.Length);
                    requestBody.Seek(0, SeekOrigin.Begin);

                    var context = new DefaultHttpContext();
                    context.Request.Body = requestBody;
                    context.Response.Body = responseBody;

                    var services = new ServiceCollection();
                    services.AddEndPoint<TestEndPoint>();
                    services.AddOptions();
                    context.Request.Path = "/foo";
                    context.Request.Method = "POST";
                    var values = new Dictionary<string, StringValues>();
                    values["id"] = connection.ConnectionId;
                    var qs = new QueryCollection(values);
                    context.Request.Query = qs;

                    var builder = new ConnectionBuilder(services.BuildServiceProvider());
                    builder.UseEndPoint<TestEndPoint>();
                    var app = builder.Build();

                    // This task should complete immediately but it exceeds the writer threshold
                    var executeTask = dispatcher.ExecuteAsync(context, new HttpSocketOptions(), app);
                    Assert.False(executeTask.IsCompleted);
                    await connection.Transport.Input.ConsumeAsync(10);
                    await executeTask.OrTimeout();

                    Assert.True(connection.Transport.Input.TryRead(out var result));
                    Assert.Equal("Hi", Encoding.UTF8.GetString(result.Buffer.ToArray()));
                    connection.Transport.Input.AdvanceTo(result.Buffer.End);
                }
            }
        }

        [Theory]
        [InlineData(TransportType.All)]
        [InlineData((TransportType)0)]
        [InlineData(TransportType.LongPolling | TransportType.WebSockets)]
        public async Task NegotiateReturnsAvailableTransportsAfterFilteringByOptions(TransportType transports)
        {
            using (StartLog(out var loggerFactory, LogLevel.Debug))
            {
                var manager = CreateConnectionManager(loggerFactory);
                var dispatcher = new HttpConnectionDispatcher(manager, loggerFactory);
                var context = new DefaultHttpContext();
                context.Features.Set<IHttpResponseFeature>(new ResponseFeature());
                var services = new ServiceCollection();
                services.AddEndPoint<TestEndPoint>();
                services.AddOptions();
                var ms = new MemoryStream();
                context.Request.Path = "/foo";
                context.Request.Method = "POST";
                context.Response.Body = ms;
                await dispatcher.ExecuteNegotiateAsync(context, new HttpSocketOptions { Transports = transports });

                var negotiateResponse = JsonConvert.DeserializeObject<JObject>(Encoding.UTF8.GetString(ms.ToArray()));
                var availableTransports = (TransportType)0;
                foreach (var transport in negotiateResponse["availableTransports"])
                {
                    var transportType = (TransportType)Enum.Parse(typeof(TransportType), transport.Value<string>("transport"));
                    availableTransports |= transportType;
                }

                Assert.Equal(transports, availableTransports);
            }
        }

        [Theory]
        [InlineData(TransportType.WebSockets)]
        [InlineData(TransportType.ServerSentEvents)]
        [InlineData(TransportType.LongPolling)]
        public async Task EndpointsThatAcceptConnectionId404WhenUnknownConnectionIdProvided(TransportType transportType)
        {
            using (StartLog(out var loggerFactory, LogLevel.Debug))
            {
                var manager = CreateConnectionManager(loggerFactory);
                var dispatcher = new HttpConnectionDispatcher(manager, loggerFactory);

                using (var strm = new MemoryStream())
                {
                    var context = new DefaultHttpContext();
                    context.Features.Set<IHttpResponseFeature>(new ResponseFeature());
                    context.Response.Body = strm;

                    var services = new ServiceCollection();
                    services.AddEndPoint<TestEndPoint>();
                    services.AddOptions();
                    context.Request.Path = "/foo";
                    context.Request.Method = "GET";
                    var values = new Dictionary<string, StringValues>();
                    values["id"] = "unknown";
                    var qs = new QueryCollection(values);
                    context.Request.Query = qs;
                    SetTransport(context, transportType);

                    var builder = new ConnectionBuilder(services.BuildServiceProvider());
                    builder.UseEndPoint<TestEndPoint>();
                    var app = builder.Build();
                    await dispatcher.ExecuteAsync(context, new HttpSocketOptions(), app);

                    Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
                    await strm.FlushAsync();
                    Assert.Equal("No Connection with that ID", Encoding.UTF8.GetString(strm.ToArray()));
                }
            }
        }

        [Fact]
        public async Task EndpointsThatAcceptConnectionId404WhenUnknownConnectionIdProvidedForPost()
        {
            using (StartLog(out var loggerFactory, LogLevel.Debug))
            {
                var manager = CreateConnectionManager(loggerFactory);
                var dispatcher = new HttpConnectionDispatcher(manager, loggerFactory);

                using (var strm = new MemoryStream())
                {
                    var context = new DefaultHttpContext();
                    context.Response.Body = strm;

                    var services = new ServiceCollection();
                    services.AddEndPoint<TestEndPoint>();
                    services.AddOptions();
                    context.Request.Path = "/foo";
                    context.Request.Method = "POST";
                    var values = new Dictionary<string, StringValues>();
                    values["id"] = "unknown";
                    var qs = new QueryCollection(values);
                    context.Request.Query = qs;

                    var builder = new ConnectionBuilder(services.BuildServiceProvider());
                    builder.UseEndPoint<TestEndPoint>();
                    var app = builder.Build();
                    await dispatcher.ExecuteAsync(context, new HttpSocketOptions(), app);

                    Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
                    await strm.FlushAsync();
                    Assert.Equal("No Connection with that ID", Encoding.UTF8.GetString(strm.ToArray()));
                }
            }
        }

        [Fact]
        public async Task PostNotAllowedForWebSocketConnections()
        {
            using (StartLog(out var loggerFactory, LogLevel.Debug))
            {
                var manager = CreateConnectionManager(loggerFactory);
                var dispatcher = new HttpConnectionDispatcher(manager, loggerFactory);
                var connection = manager.CreateConnection();
                connection.Metadata[ConnectionMetadataNames.Transport] = TransportType.WebSockets;

                using (var strm = new MemoryStream())
                {
                    var context = new DefaultHttpContext();
                    context.Response.Body = strm;

                    var services = new ServiceCollection();
                    services.AddEndPoint<TestEndPoint>();
                    services.AddOptions();
                    context.Request.Path = "/foo";
                    context.Request.Method = "POST";
                    var values = new Dictionary<string, StringValues>();
                    values["id"] = connection.ConnectionId;
                    var qs = new QueryCollection(values);
                    context.Request.Query = qs;

                    var builder = new ConnectionBuilder(services.BuildServiceProvider());
                    builder.UseEndPoint<TestEndPoint>();
                    var app = builder.Build();
                    await dispatcher.ExecuteAsync(context, new HttpSocketOptions(), app);

                    Assert.Equal(StatusCodes.Status405MethodNotAllowed, context.Response.StatusCode);
                    await strm.FlushAsync();
                    Assert.Equal("POST requests are not allowed for WebSocket connections.", Encoding.UTF8.GetString(strm.ToArray()));
                }
            }
        }

        [Theory]
        [InlineData(TransportType.LongPolling)]
        [InlineData(TransportType.ServerSentEvents)]
        public async Task PostSendsToConnection(TransportType transportType)
        {
            using (StartLog(out var loggerFactory, LogLevel.Debug))
            {
                var manager = CreateConnectionManager(loggerFactory);
                var dispatcher = new HttpConnectionDispatcher(manager, loggerFactory);
                var connection = manager.CreateConnection();
                connection.Metadata[ConnectionMetadataNames.Transport] = transportType;

                using (var requestBody = new MemoryStream())
                using (var responseBody = new MemoryStream())
                {
                    var bytes = Encoding.UTF8.GetBytes("Hello World");
                    requestBody.Write(bytes, 0, bytes.Length);
                    requestBody.Seek(0, SeekOrigin.Begin);

                    var context = new DefaultHttpContext();
                    context.Request.Body = requestBody;
                    context.Response.Body = responseBody;

                    var services = new ServiceCollection();
                    services.AddEndPoint<TestEndPoint>();
                    services.AddOptions();
                    context.Request.Path = "/foo";
                    context.Request.Method = "POST";
                    var values = new Dictionary<string, StringValues>();
                    values["id"] = connection.ConnectionId;
                    var qs = new QueryCollection(values);
                    context.Request.Query = qs;

                    var builder = new ConnectionBuilder(services.BuildServiceProvider());
                    builder.UseEndPoint<TestEndPoint>();
                    var app = builder.Build();

                    await dispatcher.ExecuteAsync(context, new HttpSocketOptions(), app);

                    Assert.True(connection.Transport.Input.TryRead(out var result));
                    Assert.Equal("Hello World", Encoding.UTF8.GetString(result.Buffer.ToArray()));
                    connection.Transport.Input.AdvanceTo(result.Buffer.End);
                }
            }
        }

        [Theory]
        [InlineData(TransportType.ServerSentEvents)]
        [InlineData(TransportType.LongPolling)]
        public async Task EndpointsThatRequireConnectionId400WhenNoConnectionIdProvided(TransportType transportType)
        {
            using (StartLog(out var loggerFactory, LogLevel.Debug))
            {
                var manager = CreateConnectionManager(loggerFactory);
                var dispatcher = new HttpConnectionDispatcher(manager, loggerFactory);
                using (var strm = new MemoryStream())
                {
                    var context = new DefaultHttpContext();
                    context.Features.Set<IHttpResponseFeature>(new ResponseFeature());
                    context.Response.Body = strm;
                    var services = new ServiceCollection();
                    services.AddOptions();
                    services.AddEndPoint<TestEndPoint>();
                    context.Request.Path = "/foo";
                    context.Request.Method = "GET";

                    SetTransport(context, transportType);

                    var builder = new ConnectionBuilder(services.BuildServiceProvider());
                    builder.UseEndPoint<TestEndPoint>();
                    var app = builder.Build();
                    await dispatcher.ExecuteAsync(context, new HttpSocketOptions(), app);

                    Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
                    await strm.FlushAsync();
                    Assert.Equal("Connection ID required", Encoding.UTF8.GetString(strm.ToArray()));
                }
            }
        }

        [Fact]
        public async Task EndpointsThatRequireConnectionId400WhenNoConnectionIdProvidedForPost()
        {
            using (StartLog(out var loggerFactory, LogLevel.Debug))
            {
                var manager = CreateConnectionManager(loggerFactory);
                var dispatcher = new HttpConnectionDispatcher(manager, loggerFactory);
                using (var strm = new MemoryStream())
                {
                    var context = new DefaultHttpContext();
                    context.Response.Body = strm;
                    var services = new ServiceCollection();
                    services.AddOptions();
                    services.AddEndPoint<TestEndPoint>();
                    context.Request.Path = "/foo";
                    context.Request.Method = "POST";

                    var builder = new ConnectionBuilder(services.BuildServiceProvider());
                    builder.UseEndPoint<TestEndPoint>();
                    var app = builder.Build();
                    await dispatcher.ExecuteAsync(context, new HttpSocketOptions(), app);

                    Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
                    await strm.FlushAsync();
                    Assert.Equal("Connection ID required", Encoding.UTF8.GetString(strm.ToArray()));
                }
            }
        }

        [Theory]
        [InlineData(TransportType.LongPolling, 204)]
        [InlineData(TransportType.WebSockets, 404)]
        [InlineData(TransportType.ServerSentEvents, 404)]
        public async Task EndPointThatOnlySupportsLongPollingRejectsOtherTransports(TransportType transportType, int status)
        {
            using (StartLog(out var loggerFactory, LogLevel.Debug))
            {
                await CheckTransportSupported(TransportType.LongPolling, transportType, status, loggerFactory);
            }
        }

        [Theory]
        [InlineData(TransportType.ServerSentEvents, 200)]
        [InlineData(TransportType.WebSockets, 404)]
        [InlineData(TransportType.LongPolling, 404)]
        public async Task EndPointThatOnlySupportsSSERejectsOtherTransports(TransportType transportType, int status)
        {
            using (StartLog(out var loggerFactory, LogLevel.Debug))
            {
                await CheckTransportSupported(TransportType.ServerSentEvents, transportType, status, loggerFactory);
            }
        }

        [Theory]
        [InlineData(TransportType.WebSockets, 200)]
        [InlineData(TransportType.ServerSentEvents, 404)]
        [InlineData(TransportType.LongPolling, 404)]
        public async Task EndPointThatOnlySupportsWebSockesRejectsOtherTransports(TransportType transportType, int status)
        {
            using (StartLog(out var loggerFactory, LogLevel.Debug))
            {
                await CheckTransportSupported(TransportType.WebSockets, transportType, status, loggerFactory);
            }
        }

        [Theory]
        [InlineData(TransportType.LongPolling, 404)]
        public async Task EndPointThatOnlySupportsWebSocketsAndSSERejectsLongPolling(TransportType transportType, int status)
        {
            using (StartLog(out var loggerFactory, LogLevel.Debug))
            {
                await CheckTransportSupported(TransportType.WebSockets | TransportType.ServerSentEvents, transportType, status, loggerFactory);
            }
        }

        [Fact]
        public async Task CompletedEndPointEndsConnection()
        {
            using (StartLog(out var loggerFactory, LogLevel.Debug))
            {
                var manager = CreateConnectionManager(loggerFactory);
                var connection = manager.CreateConnection();

                var dispatcher = new HttpConnectionDispatcher(manager, loggerFactory);

                var context = MakeRequest("/foo", connection);
                SetTransport(context, TransportType.ServerSentEvents);

                var services = new ServiceCollection();
                services.AddEndPoint<ImmediatelyCompleteEndPoint>();
                var builder = new ConnectionBuilder(services.BuildServiceProvider());
                builder.UseEndPoint<ImmediatelyCompleteEndPoint>();
                var app = builder.Build();
                await dispatcher.ExecuteAsync(context, new HttpSocketOptions(), app);

                Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);

                bool exists = manager.TryGetConnection(connection.ConnectionId, out _);
                Assert.False(exists);
            }
        }

        [Fact]
        public async Task SynchronusExceptionEndsConnection()
        {
            using (StartLog(out var loggerFactory, LogLevel.Debug))
            {
                var manager = CreateConnectionManager(loggerFactory);
                var connection = manager.CreateConnection();

                var dispatcher = new HttpConnectionDispatcher(manager, loggerFactory);
                var context = MakeRequest("/foo", connection);
                SetTransport(context, TransportType.ServerSentEvents);

                var services = new ServiceCollection();
                services.AddEndPoint<SynchronusExceptionEndPoint>();
                var builder = new ConnectionBuilder(services.BuildServiceProvider());
                builder.UseEndPoint<SynchronusExceptionEndPoint>();
                var app = builder.Build();
                await dispatcher.ExecuteAsync(context, new HttpSocketOptions(), app);

                Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);

                bool exists = manager.TryGetConnection(connection.ConnectionId, out _);
                Assert.False(exists);
            }
        }

        [Fact]
        public async Task CompletedEndPointEndsLongPollingConnection()
        {
            using (StartLog(out var loggerFactory, LogLevel.Debug))
            {
                var manager = CreateConnectionManager(loggerFactory);
                var connection = manager.CreateConnection();

                var dispatcher = new HttpConnectionDispatcher(manager, loggerFactory);

                var context = MakeRequest("/foo", connection);

                var services = new ServiceCollection();
                services.AddEndPoint<ImmediatelyCompleteEndPoint>();
                var builder = new ConnectionBuilder(services.BuildServiceProvider());
                builder.UseEndPoint<ImmediatelyCompleteEndPoint>();
                var app = builder.Build();
                await dispatcher.ExecuteAsync(context, new HttpSocketOptions(), app);

                Assert.Equal(StatusCodes.Status204NoContent, context.Response.StatusCode);

                bool exists = manager.TryGetConnection(connection.ConnectionId, out _);
                Assert.False(exists);
            }
        }

        [Fact]
        public async Task LongPollingTimeoutSets200StatusCode()
        {
            using (StartLog(out var loggerFactory, LogLevel.Debug))
            {
                var manager = CreateConnectionManager(loggerFactory);
                var connection = manager.CreateConnection();

                var dispatcher = new HttpConnectionDispatcher(manager, loggerFactory);

                var context = MakeRequest("/foo", connection);

                var services = new ServiceCollection();
                services.AddEndPoint<TestEndPoint>();
                var builder = new ConnectionBuilder(services.BuildServiceProvider());
                builder.UseEndPoint<TestEndPoint>();
                var app = builder.Build();
                var options = new HttpSocketOptions();
                options.LongPolling.PollTimeout = TimeSpan.FromSeconds(2);
                await dispatcher.ExecuteAsync(context, options, app).OrTimeout();

                Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
            }
        }

        [Fact]
        public async Task WebSocketTransportTimesOutWhenCloseFrameNotReceived()
        {
            using (StartLog(out var loggerFactory, LogLevel.Trace))
            {
                var manager = CreateConnectionManager(loggerFactory);
                var connection = manager.CreateConnection();

                var dispatcher = new HttpConnectionDispatcher(manager, loggerFactory);

                var context = MakeRequest("/foo", connection);
                SetTransport(context, TransportType.WebSockets);

                var services = new ServiceCollection();
                services.AddEndPoint<ImmediatelyCompleteEndPoint>();
                var builder = new ConnectionBuilder(services.BuildServiceProvider());
                builder.UseEndPoint<ImmediatelyCompleteEndPoint>();
                var app = builder.Build();
                var options = new HttpSocketOptions();
                options.WebSockets.CloseTimeout = TimeSpan.FromSeconds(1);

                var task = dispatcher.ExecuteAsync(context, options, app);

                await task.OrTimeout();
            }
        }

        [Theory]
        [InlineData(TransportType.WebSockets)]
        [InlineData(TransportType.ServerSentEvents)]
        public async Task RequestToActiveConnectionId409ForStreamingTransports(TransportType transportType)
        {
            using (StartLog(out var loggerFactory, LogLevel.Debug))
            {
                var manager = CreateConnectionManager(loggerFactory);
                var connection = manager.CreateConnection();

                var dispatcher = new HttpConnectionDispatcher(manager, loggerFactory);

                var context1 = MakeRequest("/foo", connection);
                var context2 = MakeRequest("/foo", connection);

                SetTransport(context1, transportType);
                SetTransport(context2, transportType);

                var services = new ServiceCollection();
                services.AddEndPoint<TestEndPoint>();
                var builder = new ConnectionBuilder(services.BuildServiceProvider());
                builder.UseEndPoint<TestEndPoint>();
                var app = builder.Build();
                var options = new HttpSocketOptions();
                var request1 = dispatcher.ExecuteAsync(context1, options, app);

                await dispatcher.ExecuteAsync(context2, options, app);

                Assert.Equal(StatusCodes.Status409Conflict, context2.Response.StatusCode);

                var webSocketTask = Task.CompletedTask;

                var ws = (TestWebSocketConnectionFeature)context1.Features.Get<IHttpWebSocketFeature>();
                if (ws != null)
                {
                    await ws.Client.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                }

                manager.CloseConnections();

                await request1.OrTimeout();
            }
        }

        [Fact]
        public async Task RequestToActiveConnectionIdKillsPreviousConnectionLongPolling()
        {
            using (StartLog(out var loggerFactory, LogLevel.Debug))
            {
                var manager = CreateConnectionManager(loggerFactory);
                var connection = manager.CreateConnection();

                var dispatcher = new HttpConnectionDispatcher(manager, loggerFactory);

                var context1 = MakeRequest("/foo", connection);
                var context2 = MakeRequest("/foo", connection);

                var services = new ServiceCollection();
                services.AddEndPoint<TestEndPoint>();
                var builder = new ConnectionBuilder(services.BuildServiceProvider());
                builder.UseEndPoint<TestEndPoint>();
                var app = builder.Build();
                var options = new HttpSocketOptions();
                var request1 = dispatcher.ExecuteAsync(context1, options, app);
                var request2 = dispatcher.ExecuteAsync(context2, options, app);

                await request1;

                Assert.Equal(StatusCodes.Status204NoContent, context1.Response.StatusCode);
                Assert.Equal(DefaultConnectionContext.ConnectionStatus.Active, connection.Status);

                Assert.False(request2.IsCompleted);

                manager.CloseConnections();

                await request2;
            }
        }

        [Theory]
        [InlineData(TransportType.ServerSentEvents)]
        [InlineData(TransportType.LongPolling)]
        public async Task RequestToDisposedConnectionIdReturns404(TransportType transportType)
        {
            using (StartLog(out var loggerFactory, LogLevel.Debug))
            {
                var manager = CreateConnectionManager(loggerFactory);
                var connection = manager.CreateConnection();
                connection.Status = DefaultConnectionContext.ConnectionStatus.Disposed;

                var dispatcher = new HttpConnectionDispatcher(manager, loggerFactory);

                var context = MakeRequest("/foo", connection);
                SetTransport(context, transportType);

                var services = new ServiceCollection();
                services.AddEndPoint<TestEndPoint>();
                var builder = new ConnectionBuilder(services.BuildServiceProvider());
                builder.UseEndPoint<TestEndPoint>();
                var app = builder.Build();
                var options = new HttpSocketOptions();
                await dispatcher.ExecuteAsync(context, options, app);


                Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
            }
        }

        [Fact]
        public async Task ConnectionStateSetToInactiveAfterPoll()
        {
            using (StartLog(out var loggerFactory, LogLevel.Debug))
            {
                var manager = CreateConnectionManager(loggerFactory);
                var connection = manager.CreateConnection();

                var dispatcher = new HttpConnectionDispatcher(manager, loggerFactory);

                var context = MakeRequest("/foo", connection);

                var services = new ServiceCollection();
                services.AddEndPoint<TestEndPoint>();
                var builder = new ConnectionBuilder(services.BuildServiceProvider());
                builder.UseEndPoint<TestEndPoint>();
                var app = builder.Build();
                var options = new HttpSocketOptions();
                var task = dispatcher.ExecuteAsync(context, options, app);

                var buffer = Encoding.UTF8.GetBytes("Hello World");

                // Write to the transport so the poll yields
                await connection.Transport.Output.WriteAsync(buffer);

                await task;

                Assert.Equal(DefaultConnectionContext.ConnectionStatus.Inactive, connection.Status);
                Assert.Null(connection.GetHttpContext());

                Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
            }
        }

        [Fact]
        public async Task BlockingConnectionWorksWithStreamingConnections()
        {
            using (StartLog(out var loggerFactory, LogLevel.Debug))
            {
                var manager = CreateConnectionManager(loggerFactory);
                var connection = manager.CreateConnection();

                var dispatcher = new HttpConnectionDispatcher(manager, loggerFactory);

                var context = MakeRequest("/foo", connection);
                SetTransport(context, TransportType.ServerSentEvents);

                var services = new ServiceCollection();
                services.AddEndPoint<BlockingEndPoint>();
                var builder = new ConnectionBuilder(services.BuildServiceProvider());
                builder.UseEndPoint<BlockingEndPoint>();
                var app = builder.Build();
                var options = new HttpSocketOptions();
                var task = dispatcher.ExecuteAsync(context, options, app);

                var buffer = Encoding.UTF8.GetBytes("Hello World");

                // Write to the application
                await connection.Application.Output.WriteAsync(buffer);

                await task;

                Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
                bool exists = manager.TryGetConnection(connection.ConnectionId, out _);
                Assert.False(exists);
            }
        }

        [Fact]
        public async Task BlockingConnectionWorksWithLongPollingConnection()
        {
            using (StartLog(out var loggerFactory, LogLevel.Debug))
            {
                var manager = CreateConnectionManager(loggerFactory);
                var connection = manager.CreateConnection();

                var dispatcher = new HttpConnectionDispatcher(manager, loggerFactory);

                var context = MakeRequest("/foo", connection);

                var services = new ServiceCollection();
                services.AddEndPoint<BlockingEndPoint>();
                var builder = new ConnectionBuilder(services.BuildServiceProvider());
                builder.UseEndPoint<BlockingEndPoint>();
                var app = builder.Build();
                var options = new HttpSocketOptions();
                var task = dispatcher.ExecuteAsync(context, options, app);

                var buffer = Encoding.UTF8.GetBytes("Hello World");

                // Write to the application
                await connection.Application.Output.WriteAsync(buffer);

                await task;

                Assert.Equal(StatusCodes.Status204NoContent, context.Response.StatusCode);
                bool exists = manager.TryGetConnection(connection.ConnectionId, out _);
                Assert.False(exists);
            }
        }

        [Fact]
        public async Task AttemptingToPollWhileAlreadyPollingReplacesTheCurrentPoll()
        {
            using (StartLog(out var loggerFactory, LogLevel.Debug))
            {
                var manager = CreateConnectionManager(loggerFactory);
                var connection = manager.CreateConnection();

                var dispatcher = new HttpConnectionDispatcher(manager, loggerFactory);

                var services = new ServiceCollection();
                services.AddEndPoint<TestEndPoint>();
                var builder = new ConnectionBuilder(services.BuildServiceProvider());
                builder.UseEndPoint<TestEndPoint>();
                var app = builder.Build();
                var options = new HttpSocketOptions();

                var context1 = MakeRequest("/foo", connection);
                var task1 = dispatcher.ExecuteAsync(context1, options, app);
                var context2 = MakeRequest("/foo", connection);
                var task2 = dispatcher.ExecuteAsync(context2, options, app);

                // Task 1 should finish when request 2 arrives
                await task1.OrTimeout();

                // Send a message from the app to complete Task 2
                await connection.Transport.Output.WriteAsync(Encoding.UTF8.GetBytes("Hello, World"));

                await task2.OrTimeout();

                // Verify the results
                Assert.Equal(StatusCodes.Status204NoContent, context1.Response.StatusCode);
                Assert.Equal(string.Empty, GetContentAsString(context1.Response.Body));
                Assert.Equal(StatusCodes.Status200OK, context2.Response.StatusCode);
                Assert.Equal("Hello, World", GetContentAsString(context2.Response.Body));
            }
        }

        [Theory]
        [InlineData(TransportType.LongPolling, null)]
        [InlineData(TransportType.ServerSentEvents, TransferFormat.Text)]
        [InlineData(TransportType.WebSockets, TransferFormat.Binary | TransferFormat.Text)]
        public async Task TransferModeSet(TransportType transportType, TransferFormat? expectedTransferFormats)
        {
            using (StartLog(out var loggerFactory, LogLevel.Debug))
            {
                var manager = CreateConnectionManager(loggerFactory);
                var connection = manager.CreateConnection();

                var dispatcher = new HttpConnectionDispatcher(manager, loggerFactory);

                var context = MakeRequest("/foo", connection);
                SetTransport(context, transportType);

                var services = new ServiceCollection();
                services.AddEndPoint<ImmediatelyCompleteEndPoint>();
                var builder = new ConnectionBuilder(services.BuildServiceProvider());
                builder.UseEndPoint<ImmediatelyCompleteEndPoint>();
                var app = builder.Build();

                var options = new HttpSocketOptions();
                options.WebSockets.CloseTimeout = TimeSpan.FromSeconds(0);
                await dispatcher.ExecuteAsync(context, options, app);

                if (expectedTransferFormats != null)
                {
                    var transferFormatFeature = connection.Features.Get<ITransferFormatFeature>();
                    Assert.Equal(expectedTransferFormats.Value, transferFormatFeature.SupportedFormats);
                }
            }
        }

        [Fact]
        public async Task UnauthorizedConnectionFailsToStartEndPoint()
        {
            using (StartLog(out var loggerFactory, LogLevel.Debug))
            {
                var manager = CreateConnectionManager(loggerFactory);
                var connection = manager.CreateConnection();
                var dispatcher = new HttpConnectionDispatcher(manager, loggerFactory);
                var context = new DefaultHttpContext();
                var services = new ServiceCollection();
                services.AddOptions();
                services.AddEndPoint<TestEndPoint>();
                services.AddAuthorizationPolicyEvaluator();
                services.AddAuthorization(o =>
                {
                    o.AddPolicy("test", policy => policy.RequireClaim(ClaimTypes.NameIdentifier));
                });
                services.AddAuthenticationCore(o =>
                {
                    o.DefaultScheme = "Default";
                    o.AddScheme("Default", a => a.HandlerType = typeof(TestAuthenticationHandler));
                });
                services.AddLogging();
                var sp = services.BuildServiceProvider();
                context.Request.Path = "/foo";
                context.Request.Method = "GET";
                context.RequestServices = sp;
                var values = new Dictionary<string, StringValues>();
                values["id"] = connection.ConnectionId;
                var qs = new QueryCollection(values);
                context.Request.Query = qs;

                var builder = new ConnectionBuilder(sp);
                builder.UseEndPoint<TestEndPoint>();
                var app = builder.Build();
                var options = new HttpSocketOptions();
                options.AuthorizationData.Add(new AuthorizeAttribute("test"));

                // would hang if EndPoint was running
                await dispatcher.ExecuteAsync(context, options, app).OrTimeout();

                Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
            }
        }

        [Fact]
        public async Task AuthenticatedUserWithoutPermissionCausesForbidden()
        {
            using (StartLog(out var loggerFactory, LogLevel.Debug))
            {
                var manager = CreateConnectionManager(loggerFactory);
                var connection = manager.CreateConnection();
                var dispatcher = new HttpConnectionDispatcher(manager, loggerFactory);
                var context = new DefaultHttpContext();
                var services = new ServiceCollection();
                services.AddOptions();
                services.AddEndPoint<TestEndPoint>();
                services.AddAuthorizationPolicyEvaluator();
                services.AddAuthorization(o =>
                {
                    o.AddPolicy("test", policy => policy.RequireClaim(ClaimTypes.NameIdentifier));
                });
                services.AddAuthenticationCore(o =>
                {
                    o.DefaultScheme = "Default";
                    o.AddScheme("Default", a => a.HandlerType = typeof(TestAuthenticationHandler));
                });
                services.AddLogging();
                var sp = services.BuildServiceProvider();
                context.Request.Path = "/foo";
                context.Request.Method = "GET";
                context.RequestServices = sp;
                var values = new Dictionary<string, StringValues>();
                values["id"] = connection.ConnectionId;
                var qs = new QueryCollection(values);
                context.Request.Query = qs;

                var builder = new ConnectionBuilder(sp);
                builder.UseEndPoint<TestEndPoint>();
                var app = builder.Build();
                var options = new HttpSocketOptions();
                options.AuthorizationData.Add(new AuthorizeAttribute("test"));

                context.User = new ClaimsPrincipal(new ClaimsIdentity("authenticated"));

                // would hang if EndPoint was running
                await dispatcher.ExecuteAsync(context, options, app).OrTimeout();

                Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
            }
        }

        [Fact]
        public async Task AuthorizedConnectionCanConnectToEndPoint()
        {
            using (StartLog(out var loggerFactory, LogLevel.Debug))
            {
                var manager = CreateConnectionManager(loggerFactory);
                var connection = manager.CreateConnection();
                var dispatcher = new HttpConnectionDispatcher(manager, loggerFactory);
                var context = new DefaultHttpContext();
                context.Features.Set<IHttpResponseFeature>(new ResponseFeature());
                var services = new ServiceCollection();
                services.AddOptions();
                services.AddEndPoint<TestEndPoint>();
                services.AddAuthorizationPolicyEvaluator();
                services.AddAuthorization(o =>
                {
                    o.AddPolicy("test", policy =>
                    {
                        policy.RequireClaim(ClaimTypes.NameIdentifier);
                    });
                });
                services.AddLogging();
                services.AddAuthenticationCore(o =>
                {
                    o.DefaultScheme = "Default";
                    o.AddScheme("Default", a => a.HandlerType = typeof(TestAuthenticationHandler));
                });
                var sp = services.BuildServiceProvider();
                context.Request.Path = "/foo";
                context.Request.Method = "GET";
                context.RequestServices = sp;
                var values = new Dictionary<string, StringValues>();
                values["id"] = connection.ConnectionId;
                var qs = new QueryCollection(values);
                context.Request.Query = qs;
                context.Response.Body = new MemoryStream();

                var builder = new ConnectionBuilder(sp);
                builder.UseEndPoint<TestEndPoint>();
                var app = builder.Build();
                var options = new HttpSocketOptions();
                options.AuthorizationData.Add(new AuthorizeAttribute("test"));

                // "authorize" user
                context.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "name") }));

                var endPointTask = dispatcher.ExecuteAsync(context, options, app);
                await connection.Transport.Output.WriteAsync(Encoding.UTF8.GetBytes("Hello, World")).AsTask().OrTimeout();

                await endPointTask.OrTimeout();

                Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
                Assert.Equal("Hello, World", GetContentAsString(context.Response.Body));
            }
        }

        [Fact]
        public async Task AllPoliciesRequiredForAuthorizedEndPoint()
        {
            using (StartLog(out var loggerFactory, LogLevel.Debug))
            {
                var manager = CreateConnectionManager(loggerFactory);
                var connection = manager.CreateConnection();
                var dispatcher = new HttpConnectionDispatcher(manager, loggerFactory);
                var context = new DefaultHttpContext();
                context.Features.Set<IHttpResponseFeature>(new ResponseFeature());
                var services = new ServiceCollection();
                services.AddOptions();
                services.AddEndPoint<TestEndPoint>();
                services.AddAuthorizationPolicyEvaluator();
                services.AddAuthorization(o =>
                {
                    o.AddPolicy("test", policy =>
                    {
                        policy.RequireClaim(ClaimTypes.NameIdentifier);
                    });
                    o.AddPolicy("secondPolicy", policy =>
                    {
                        policy.RequireClaim(ClaimTypes.StreetAddress);
                    });
                });
                services.AddLogging();
                services.AddAuthenticationCore(o =>
                {
                    o.DefaultScheme = "Default";
                    o.AddScheme("Default", a => a.HandlerType = typeof(TestAuthenticationHandler));
                });
                var sp = services.BuildServiceProvider();
                context.Request.Path = "/foo";
                context.Request.Method = "GET";
                context.RequestServices = sp;
                var values = new Dictionary<string, StringValues>();
                values["id"] = connection.ConnectionId;
                var qs = new QueryCollection(values);
                context.Request.Query = qs;
                context.Response.Body = new MemoryStream();

                var builder = new ConnectionBuilder(sp);
                builder.UseEndPoint<TestEndPoint>();
                var app = builder.Build();
                var options = new HttpSocketOptions();
                options.AuthorizationData.Add(new AuthorizeAttribute("test"));
                options.AuthorizationData.Add(new AuthorizeAttribute("secondPolicy"));

                // partially "authorize" user
                context.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "name") }));

                // would hang if EndPoint was running
                await dispatcher.ExecuteAsync(context, options, app).OrTimeout();

                Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);

                // reset HttpContext
                context = new DefaultHttpContext();
                context.Features.Set<IHttpResponseFeature>(new ResponseFeature());
                context.Request.Path = "/foo";
                context.Request.Method = "GET";
                context.RequestServices = sp;
                context.Request.Query = qs;
                context.Response.Body = new MemoryStream();
                // fully "authorize" user
                context.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                new Claim(ClaimTypes.NameIdentifier, "name"),
                new Claim(ClaimTypes.StreetAddress, "12345 123rd St. NW")
            }));

                var endPointTask = dispatcher.ExecuteAsync(context, options, app);
                await connection.Transport.Output.WriteAsync(Encoding.UTF8.GetBytes("Hello, World")).AsTask().OrTimeout();

                await endPointTask.OrTimeout();

                Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
                Assert.Equal("Hello, World", GetContentAsString(context.Response.Body));
            }
        }

        [Fact]
        public async Task AuthorizedConnectionWithAcceptedSchemesCanConnectToEndPoint()
        {
            using (StartLog(out var loggerFactory, LogLevel.Debug))
            {
                var manager = CreateConnectionManager(loggerFactory);
                var connection = manager.CreateConnection();
                var dispatcher = new HttpConnectionDispatcher(manager, loggerFactory);
                var context = new DefaultHttpContext();
                context.Features.Set<IHttpResponseFeature>(new ResponseFeature());
                var services = new ServiceCollection();
                services.AddOptions();
                services.AddEndPoint<TestEndPoint>();
                services.AddAuthorization(o =>
                {
                    o.AddPolicy("test", policy =>
                    {
                        policy.RequireClaim(ClaimTypes.NameIdentifier);
                        policy.AddAuthenticationSchemes("Default");
                    });
                });
                services.AddAuthorizationPolicyEvaluator();
                services.AddLogging();
                services.AddAuthenticationCore(o =>
                {
                    o.DefaultScheme = "Default";
                    o.AddScheme("Default", a => a.HandlerType = typeof(TestAuthenticationHandler));
                });
                var sp = services.BuildServiceProvider();
                context.Request.Path = "/foo";
                context.Request.Method = "GET";
                context.RequestServices = sp;
                var values = new Dictionary<string, StringValues>();
                values["id"] = connection.ConnectionId;
                var qs = new QueryCollection(values);
                context.Request.Query = qs;
                context.Response.Body = new MemoryStream();

                var builder = new ConnectionBuilder(sp);
                builder.UseEndPoint<TestEndPoint>();
                var app = builder.Build();
                var options = new HttpSocketOptions();
                options.AuthorizationData.Add(new AuthorizeAttribute("test"));

                // "authorize" user
                context.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "name") }));

                var endPointTask = dispatcher.ExecuteAsync(context, options, app);
                await connection.Transport.Output.WriteAsync(Encoding.UTF8.GetBytes("Hello, World")).AsTask().OrTimeout();

                await endPointTask.OrTimeout();

                Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
                Assert.Equal("Hello, World", GetContentAsString(context.Response.Body));
            }
        }

        [Fact]
        public async Task AuthorizedConnectionWithRejectedSchemesFailsToConnectToEndPoint()
        {
            using (StartLog(out var loggerFactory, LogLevel.Debug))
            {
                var manager = CreateConnectionManager(loggerFactory);
                var connection = manager.CreateConnection();
                var dispatcher = new HttpConnectionDispatcher(manager, loggerFactory);
                var context = new DefaultHttpContext();
                var services = new ServiceCollection();
                services.AddOptions();
                services.AddEndPoint<TestEndPoint>();
                services.AddAuthorization(o =>
                {
                    o.AddPolicy("test", policy =>
                    {
                        policy.RequireClaim(ClaimTypes.NameIdentifier);
                        policy.AddAuthenticationSchemes("Default");
                    });
                });
                services.AddAuthorizationPolicyEvaluator();
                services.AddLogging();
                services.AddAuthenticationCore(o =>
                {
                    o.DefaultScheme = "Default";
                    o.AddScheme("Default", a => a.HandlerType = typeof(RejectHandler));
                });
                var sp = services.BuildServiceProvider();
                context.Request.Path = "/foo";
                context.Request.Method = "GET";
                context.RequestServices = sp;
                var values = new Dictionary<string, StringValues>();
                values["id"] = connection.ConnectionId;
                var qs = new QueryCollection(values);
                context.Request.Query = qs;
                context.Response.Body = new MemoryStream();

                var builder = new ConnectionBuilder(sp);
                builder.UseEndPoint<TestEndPoint>();
                var app = builder.Build();
                var options = new HttpSocketOptions();
                options.AuthorizationData.Add(new AuthorizeAttribute("test"));

                // "authorize" user
                context.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "name") }));

                // would block if EndPoint was executed
                await dispatcher.ExecuteAsync(context, options, app).OrTimeout();

                Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
            }
        }

        [Fact]
        public async Task SetsInherentKeepAliveFeatureOnFirstLongPollingRequest()
        {
            using (StartLog(out var loggerFactory, LogLevel.Debug))
            {
                var manager = CreateConnectionManager(loggerFactory);
                var connection = manager.CreateConnection();

                var dispatcher = new HttpConnectionDispatcher(manager, loggerFactory);

                var context = MakeRequest("/foo", connection);

                var services = new ServiceCollection();
                services.AddEndPoint<TestEndPoint>();
                var builder = new ConnectionBuilder(services.BuildServiceProvider());
                builder.UseEndPoint<TestEndPoint>();
                var app = builder.Build();
                var options = new HttpSocketOptions();
                options.LongPolling.PollTimeout = TimeSpan.FromMilliseconds(1); // We don't care about the poll itself

                Assert.Null(connection.Features.Get<IConnectionInherentKeepAliveFeature>());

                await dispatcher.ExecuteAsync(context, options, app).OrTimeout();

                Assert.NotNull(connection.Features.Get<IConnectionInherentKeepAliveFeature>());
                Assert.Equal(options.LongPolling.PollTimeout, connection.Features.Get<IConnectionInherentKeepAliveFeature>().KeepAliveInterval);
            }
        }

        private class RejectHandler : TestAuthenticationHandler
        {
            protected override bool ShouldAccept => false;
        }

        private class TestAuthenticationHandler : IAuthenticationHandler
        {
            private HttpContext HttpContext;
            private AuthenticationScheme _scheme;

            protected virtual bool ShouldAccept { get => true; }

            public Task<AuthenticateResult> AuthenticateAsync()
            {
                if (ShouldAccept)
                {
                    return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(HttpContext.User, _scheme.Name)));
                }
                else
                {
                    return Task.FromResult(AuthenticateResult.NoResult());
                }
            }

            public Task ChallengeAsync(AuthenticationProperties properties)
            {
                HttpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            }

            public Task ForbidAsync(AuthenticationProperties properties)
            {
                HttpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            }

            public Task InitializeAsync(AuthenticationScheme scheme, HttpContext context)
            {
                HttpContext = context;
                _scheme = scheme;
                return Task.CompletedTask;
            }
        }

        private static async Task CheckTransportSupported(TransportType supportedTransports, TransportType transportType, int status, ILoggerFactory loggerFactory)
        {
            var manager = CreateConnectionManager(loggerFactory);
            var connection = manager.CreateConnection();
            var dispatcher = new HttpConnectionDispatcher(manager, loggerFactory);
            using (var strm = new MemoryStream())
            {
                var context = new DefaultHttpContext();
                context.Features.Set<IHttpResponseFeature>(new ResponseFeature());
                context.Response.Body = strm;
                var services = new ServiceCollection();
                services.AddOptions();
                services.AddEndPoint<ImmediatelyCompleteEndPoint>();
                SetTransport(context, transportType);
                context.Request.Path = "/foo";
                context.Request.Method = "GET";
                var values = new Dictionary<string, StringValues>();
                values["id"] = connection.ConnectionId;
                var qs = new QueryCollection(values);
                context.Request.Query = qs;

                var builder = new ConnectionBuilder(services.BuildServiceProvider());
                builder.UseEndPoint<ImmediatelyCompleteEndPoint>();
                var app = builder.Build();
                var options = new HttpSocketOptions();
                options.Transports = supportedTransports;

                await dispatcher.ExecuteAsync(context, options, app);
                Assert.Equal(status, context.Response.StatusCode);
                await strm.FlushAsync();

                // Check the message for 404
                if (status == 404)
                {
                    Assert.Equal($"{transportType} transport not supported by this end point type", Encoding.UTF8.GetString(strm.ToArray()));
                }
            }
        }

        private static DefaultHttpContext MakeRequest(string path, DefaultConnectionContext connection, string format = null)
        {
            var context = new DefaultHttpContext();
            context.Features.Set<IHttpResponseFeature>(new ResponseFeature());
            context.Request.Path = path;
            context.Request.Method = "GET";
            var values = new Dictionary<string, StringValues>();
            values["id"] = connection.ConnectionId;
            if (format != null)
            {
                values["format"] = format;
            }
            var qs = new QueryCollection(values);
            context.Request.Query = qs;
            context.Response.Body = new MemoryStream();
            return context;
        }

        private static void SetTransport(HttpContext context, TransportType transportType)
        {
            switch (transportType)
            {
                case TransportType.WebSockets:
                    context.Features.Set<IHttpWebSocketFeature>(new TestWebSocketConnectionFeature());
                    break;
                case TransportType.ServerSentEvents:
                    context.Request.Headers["Accept"] = "text/event-stream";
                    break;
                default:
                    break;
            }
        }

        private static ConnectionManager CreateConnectionManager(ILoggerFactory loggerFactory)
        {
            return new ConnectionManager(new Logger<ConnectionManager>(loggerFactory ?? new LoggerFactory()), new EmptyApplicationLifetime());
        }

        private string GetContentAsString(Stream body)
        {
            Assert.True(body.CanSeek, "Can't get content of a non-seekable stream");
            body.Seek(0, SeekOrigin.Begin);
            using (var reader = new StreamReader(body))
            {
                return reader.ReadToEnd();
            }
        }
    }

    public class NerverEndingEndPoint : EndPoint
    {
        public override Task OnConnectedAsync(ConnectionContext connection)
        {
            var tcs = new TaskCompletionSource<object>();
            return tcs.Task;
        }
    }

    public class BlockingEndPoint : EndPoint
    {
        public override Task OnConnectedAsync(ConnectionContext connection)
        {
            var result = connection.Transport.Input.ReadAsync().AsTask().Result;
            connection.Transport.Input.AdvanceTo(result.Buffer.End);
            return Task.CompletedTask;
        }
    }

    public class SynchronusExceptionEndPoint : EndPoint
    {
        public override Task OnConnectedAsync(ConnectionContext connection)
        {
            throw new InvalidOperationException();
        }
    }

    public class ImmediatelyCompleteEndPoint : EndPoint
    {
        public override Task OnConnectedAsync(ConnectionContext connection)
        {
            return Task.CompletedTask;
        }
    }

    public class TestEndPoint : EndPoint
    {
        public override async Task OnConnectedAsync(ConnectionContext connection)
        {
            while (true)
            {
                var result = await connection.Transport.Input.ReadAsync();

                try
                {
                    if (result.IsCompleted)
                    {
                        break;
                    }
                }
                finally
                {
                    connection.Transport.Input.AdvanceTo(result.Buffer.End);
                }
            }
        }
    }

    public class ResponseFeature : HttpResponseFeature
    {
        public override void OnCompleted(Func<object, Task> callback, object state)
        {
        }

        public override void OnStarting(Func<object, Task> callback, object state)
        {
        }
    }
}
