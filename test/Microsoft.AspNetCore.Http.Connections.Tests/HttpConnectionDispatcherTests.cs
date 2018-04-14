// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Http.Connections.Internal;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Primitives;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.AspNetCore.Http.Connections.Tests
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
                services.AddSingleton<TestConnectionHandler>();
                services.AddOptions();
                var ms = new MemoryStream();
                context.Request.Path = "/foo";
                context.Request.Method = "POST";
                context.Response.Body = ms;
                await dispatcher.ExecuteNegotiateAsync(context, new HttpConnectionDispatcherOptions());
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
                services.AddSingleton<TestConnectionHandler>();
                services.AddOptions();
                var ms = new MemoryStream();
                context.Request.Path = "/foo";
                context.Request.Method = "POST";
                context.Response.Body = ms;
                var options = new HttpConnectionDispatcherOptions { TransportMaxBufferSize = 4, ApplicationMaxBufferSize = 4 };
                await dispatcher.ExecuteNegotiateAsync(context, options);
                var negotiateResponse = JsonConvert.DeserializeObject<JObject>(Encoding.UTF8.GetString(ms.ToArray()));
                var connectionId = negotiateResponse.Value<string>("connectionId");
                context.Request.QueryString = context.Request.QueryString.Add("id", connectionId);
                Assert.True(manager.TryGetConnection(connectionId, out var connection));
                // Fake actual connection after negotiate to populate the pipes on the connection
                await dispatcher.ExecuteAsync(context, options, c => Task.CompletedTask);

                // This write should complete immediately but it exceeds the writer threshold
                var writeTask = connection.Application.Output.WriteAsync(new[] { (byte)'b', (byte)'y', (byte)'t', (byte)'e', (byte)'s' });

                Assert.False(writeTask.IsCompleted);

                // Reading here puts us below the threshold
                await connection.Transport.Input.ConsumeAsync(5);

                await writeTask.AsTask().OrTimeout();
            }
        }

        [Theory]
        [InlineData(HttpTransportType.LongPolling)]
        [InlineData(HttpTransportType.ServerSentEvents)]
        public async Task CheckThatThresholdValuesAreEnforcedWithSends(HttpTransportType transportType)
        {
            using (StartLog(out var loggerFactory, LogLevel.Debug))
            {
                var manager = CreateConnectionManager(loggerFactory);
                var dispatcher = new HttpConnectionDispatcher(manager, loggerFactory);
                var pipeOptions = new PipeOptions(pauseWriterThreshold: 8, resumeWriterThreshold: 4);
                var connection = manager.CreateConnection(pipeOptions, pipeOptions);
                connection.TransportType = transportType;

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
                    services.AddSingleton<TestConnectionHandler>();
                    services.AddOptions();
                    context.Request.Path = "/foo";
                    context.Request.Method = "POST";
                    var values = new Dictionary<string, StringValues>();
                    values["id"] = connection.ConnectionId;
                    var qs = new QueryCollection(values);
                    context.Request.Query = qs;

                    var builder = new ConnectionBuilder(services.BuildServiceProvider());
                    builder.UseConnectionHandler<TestConnectionHandler>();
                    var app = builder.Build();

                    // This task should complete immediately but it exceeds the writer threshold
                    var executeTask = dispatcher.ExecuteAsync(context, new HttpConnectionDispatcherOptions(), app);
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
        [InlineData(HttpTransportType.LongPolling | HttpTransportType.WebSockets | HttpTransportType.ServerSentEvents)]
        [InlineData(HttpTransportType.None)]
        [InlineData(HttpTransportType.LongPolling | HttpTransportType.WebSockets)]
        public async Task NegotiateReturnsAvailableTransportsAfterFilteringByOptions(HttpTransportType transports)
        {
            using (StartLog(out var loggerFactory, LogLevel.Debug))
            {
                var manager = CreateConnectionManager(loggerFactory);
                var dispatcher = new HttpConnectionDispatcher(manager, loggerFactory);
                var context = new DefaultHttpContext();
                context.Features.Set<IHttpResponseFeature>(new ResponseFeature());
                context.Features.Set<IHttpWebSocketFeature>(new TestWebSocketConnectionFeature());
                var services = new ServiceCollection();
                services.AddSingleton<TestConnectionHandler>();
                services.AddOptions();
                var ms = new MemoryStream();
                context.Request.Path = "/foo";
                context.Request.Method = "POST";
                context.Response.Body = ms;
                await dispatcher.ExecuteNegotiateAsync(context, new HttpConnectionDispatcherOptions { Transports = transports });

                var negotiateResponse = JsonConvert.DeserializeObject<JObject>(Encoding.UTF8.GetString(ms.ToArray()));
                var availableTransports = HttpTransportType.None;
                foreach (var transport in negotiateResponse["availableTransports"])
                {
                    var transportType = (HttpTransportType)Enum.Parse(typeof(HttpTransportType), transport.Value<string>("transport"));
                    availableTransports |= transportType;
                }

                Assert.Equal(transports, availableTransports);
            }
        }

        [Theory]
        [InlineData(HttpTransportType.WebSockets)]
        [InlineData(HttpTransportType.ServerSentEvents)]
        [InlineData(HttpTransportType.LongPolling)]
        public async Task EndpointsThatAcceptConnectionId404WhenUnknownConnectionIdProvided(HttpTransportType transportType)
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
                    services.AddSingleton<TestConnectionHandler>();
                    services.AddOptions();
                    context.Request.Path = "/foo";
                    context.Request.Method = "GET";
                    var values = new Dictionary<string, StringValues>();
                    values["id"] = "unknown";
                    var qs = new QueryCollection(values);
                    context.Request.Query = qs;
                    SetTransport(context, transportType);

                    var builder = new ConnectionBuilder(services.BuildServiceProvider());
                    builder.UseConnectionHandler<TestConnectionHandler>();
                    var app = builder.Build();
                    await dispatcher.ExecuteAsync(context, new HttpConnectionDispatcherOptions(), app);

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
                    services.AddSingleton<TestConnectionHandler>();
                    services.AddOptions();
                    context.Request.Path = "/foo";
                    context.Request.Method = "POST";
                    var values = new Dictionary<string, StringValues>();
                    values["id"] = "unknown";
                    var qs = new QueryCollection(values);
                    context.Request.Query = qs;

                    var builder = new ConnectionBuilder(services.BuildServiceProvider());
                    builder.UseConnectionHandler<TestConnectionHandler>();
                    var app = builder.Build();
                    await dispatcher.ExecuteAsync(context, new HttpConnectionDispatcherOptions(), app);

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
                connection.TransportType = HttpTransportType.WebSockets;

                using (var strm = new MemoryStream())
                {
                    var context = new DefaultHttpContext();
                    context.Response.Body = strm;

                    var services = new ServiceCollection();
                    services.AddSingleton<TestConnectionHandler>();
                    services.AddOptions();
                    context.Request.Path = "/foo";
                    context.Request.Method = "POST";
                    var values = new Dictionary<string, StringValues>();
                    values["id"] = connection.ConnectionId;
                    var qs = new QueryCollection(values);
                    context.Request.Query = qs;

                    var builder = new ConnectionBuilder(services.BuildServiceProvider());
                    builder.UseConnectionHandler<TestConnectionHandler>();
                    var app = builder.Build();
                    await dispatcher.ExecuteAsync(context, new HttpConnectionDispatcherOptions(), app);

                    Assert.Equal(StatusCodes.Status405MethodNotAllowed, context.Response.StatusCode);
                    await strm.FlushAsync();
                    Assert.Equal("POST requests are not allowed for WebSocket connections.", Encoding.UTF8.GetString(strm.ToArray()));
                }
            }
        }

        [Fact]
        public async Task PostReturns404IfConnectionDisposed()
        {
            using (StartLog(out var loggerFactory, LogLevel.Debug))
            {
                var manager = CreateConnectionManager(loggerFactory);
                var dispatcher = new HttpConnectionDispatcher(manager, loggerFactory);
                var connection = manager.CreateConnection();
                connection.TransportType = HttpTransportType.LongPolling;
                await connection.DisposeAsync(closeGracefully: false);

                using (var strm = new MemoryStream())
                {
                    var context = new DefaultHttpContext();
                    context.Response.Body = strm;

                    var services = new ServiceCollection();
                    services.AddSingleton<TestConnectionHandler>();
                    services.AddOptions();
                    context.Request.Path = "/foo";
                    context.Request.Method = "POST";
                    var values = new Dictionary<string, StringValues>();
                    values["id"] = connection.ConnectionId;
                    var qs = new QueryCollection(values);
                    context.Request.Query = qs;

                    var builder = new ConnectionBuilder(services.BuildServiceProvider());
                    builder.UseConnectionHandler<TestConnectionHandler>();
                    var app = builder.Build();
                    await dispatcher.ExecuteAsync(context, new HttpConnectionDispatcherOptions(), app);

                    Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
                }
            }
        }

        [Theory]
        [InlineData(HttpTransportType.ServerSentEvents)]
        [InlineData(HttpTransportType.WebSockets)]
        public async Task TransportEndingGracefullyWaitsOnApplication(HttpTransportType transportType)
        {
            using (StartLog(out var loggerFactory, LogLevel.Debug))
            {
                var manager = CreateConnectionManager(loggerFactory);
                var dispatcher = new HttpConnectionDispatcher(manager, loggerFactory);
                var connection = manager.CreateConnection();
                connection.TransportType = transportType;

                using (var strm = new MemoryStream())
                {
                    var context = new DefaultHttpContext();
                    SetTransport(context, transportType);
                    var cts = new CancellationTokenSource();
                    context.Response.Body = strm;
                    context.RequestAborted = cts.Token;

                    var services = new ServiceCollection();
                    services.AddSingleton<TestConnectionHandler>();
                    services.AddOptions();
                    context.Request.Path = "/foo";
                    context.Request.Method = "GET";
                    var values = new Dictionary<string, StringValues>();
                    values["id"] = connection.ConnectionId;
                    var qs = new QueryCollection(values);
                    context.Request.Query = qs;

                    var builder = new ConnectionBuilder(services.BuildServiceProvider());
                    builder.Use(next =>
                    {
                        return async connectionContext =>
                        {
                            // Ensure both sides of the pipe are ok
                            var result = await connectionContext.Transport.Input.ReadAsync();
                            Assert.True(result.IsCompleted);
                            await connectionContext.Transport.Output.WriteAsync(result.Buffer.First);
                        };
                    });

                    var app = builder.Build();
                    var task = dispatcher.ExecuteAsync(context, new HttpConnectionDispatcherOptions(), app);

                    // Pretend the transport closed because the client disconnected
                    if (context.WebSockets.IsWebSocketRequest)
                    {
                        var ws = (TestWebSocketConnectionFeature)context.Features.Get<IHttpWebSocketFeature>();
                        await ws.Client.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", default);
                    }
                    else
                    {
                        cts.Cancel();
                    }

                    await task.OrTimeout();

                    await connection.ApplicationTask.OrTimeout();
                }
            }
        }

        [Fact]
        public async Task TransportEndingGracefullyWaitsOnApplicationLongPolling()
        {
            using (StartLog(out var loggerFactory, LogLevel.Debug))
            {
                var manager = CreateConnectionManager(loggerFactory);
                var dispatcher = new HttpConnectionDispatcher(manager, loggerFactory);
                var connection = manager.CreateConnection();
                connection.TransportType = HttpTransportType.LongPolling;

                using (var strm = new MemoryStream())
                {
                    var context = new DefaultHttpContext();
                    SetTransport(context, HttpTransportType.LongPolling);
                    var cts = new CancellationTokenSource();
                    context.Response.Body = strm;
                    context.RequestAborted = cts.Token;

                    var services = new ServiceCollection();
                    services.AddSingleton<TestConnectionHandler>();
                    services.AddOptions();
                    context.Request.Path = "/foo";
                    context.Request.Method = "GET";
                    var values = new Dictionary<string, StringValues>();
                    values["id"] = connection.ConnectionId;
                    var qs = new QueryCollection(values);
                    context.Request.Query = qs;

                    var builder = new ConnectionBuilder(services.BuildServiceProvider());
                    builder.Use(next =>
                    {
                        return async connectionContext =>
                        {
                            // Ensure both sides of the pipe are ok
                            var result = await connectionContext.Transport.Input.ReadAsync();
                            Assert.True(result.IsCompleted);
                            await connectionContext.Transport.Output.WriteAsync(result.Buffer.First);
                        };
                    });

                    var app = builder.Build();
                    var task = dispatcher.ExecuteAsync(context, new HttpConnectionDispatcherOptions(), app);

                    // Pretend the transport closed because the client disconnected
                    cts.Cancel();

                    await task.OrTimeout();

                    // We've been gone longer than the expiration time
                    connection.LastSeenUtc = DateTime.UtcNow.Subtract(TimeSpan.FromSeconds(10));

                    // The application is still running here because the poll is only killed
                    // by the heartbeat so we pretend to do a scan and this should force the application task to complete
                    await manager.ScanAsync();

                    // The application task should complete gracefully
                    await connection.ApplicationTask.OrTimeout();
                }
            }
        }

        [Theory]
        [InlineData(HttpTransportType.LongPolling)]
        [InlineData(HttpTransportType.ServerSentEvents)]
        public async Task PostSendsToConnection(HttpTransportType transportType)
        {
            using (StartLog(out var loggerFactory, LogLevel.Debug))
            {
                var manager = CreateConnectionManager(loggerFactory);
                var dispatcher = new HttpConnectionDispatcher(manager, loggerFactory);
                var connection = manager.CreateConnection();
                connection.TransportType = transportType;

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
                    services.AddSingleton<TestConnectionHandler>();
                    services.AddOptions();
                    context.Request.Path = "/foo";
                    context.Request.Method = "POST";
                    var values = new Dictionary<string, StringValues>();
                    values["id"] = connection.ConnectionId;
                    var qs = new QueryCollection(values);
                    context.Request.Query = qs;

                    var builder = new ConnectionBuilder(services.BuildServiceProvider());
                    builder.UseConnectionHandler<TestConnectionHandler>();
                    var app = builder.Build();

                    Assert.Equal(0, connection.ApplicationStream.Length);

                    await dispatcher.ExecuteAsync(context, new HttpConnectionDispatcherOptions(), app);

                    Assert.True(connection.Transport.Input.TryRead(out var result));
                    Assert.Equal("Hello World", Encoding.UTF8.GetString(result.Buffer.ToArray()));
                    Assert.Equal(0, connection.ApplicationStream.Length);
                    connection.Transport.Input.AdvanceTo(result.Buffer.End);
                }
            }
        }

        [Theory]
        [InlineData(HttpTransportType.LongPolling)]
        [InlineData(HttpTransportType.ServerSentEvents)]
        public async Task PostSendsToConnectionInParallel(HttpTransportType transportType)
        {
            using (StartLog(out var loggerFactory, LogLevel.Debug))
            {
                var manager = CreateConnectionManager(loggerFactory);
                var dispatcher = new HttpConnectionDispatcher(manager, loggerFactory);
                var connection = manager.CreateConnection();
                connection.TransportType = transportType;

                // Allow a maximum of one caller to use code at one time
                var callerTracker = new SemaphoreSlim(1, 1);
                var waitTcs = new TaskCompletionSource<bool>();

                // This tests thread safety of sending multiple pieces of data to a connection at once
                var executeTask1 = DispatcherExecuteAsync(dispatcher, connection, callerTracker, waitTcs.Task);
                var executeTask2 = DispatcherExecuteAsync(dispatcher, connection, callerTracker, waitTcs.Task);

                waitTcs.SetResult(true);

                await Task.WhenAll(executeTask1, executeTask2);
            }

            async Task DispatcherExecuteAsync(HttpConnectionDispatcher dispatcher, HttpConnectionContext connection, SemaphoreSlim callerTracker, Task waitTask)
            {
                using (var requestBody = new TrackingMemoryStream(callerTracker, waitTask))
                {
                    var bytes = Encoding.UTF8.GetBytes("Hello World");
                    requestBody.Write(bytes, 0, bytes.Length);
                    requestBody.Seek(0, SeekOrigin.Begin);

                    var context = new DefaultHttpContext();
                    context.Request.Body = requestBody;

                    var services = new ServiceCollection();
                    services.AddSingleton<TestConnectionHandler>();
                    services.AddOptions();
                    context.Request.Path = "/foo";
                    context.Request.Method = "POST";
                    var values = new Dictionary<string, StringValues>();
                    values["id"] = connection.ConnectionId;
                    var qs = new QueryCollection(values);
                    context.Request.Query = qs;

                    var builder = new ConnectionBuilder(services.BuildServiceProvider());
                    builder.UseConnectionHandler<TestConnectionHandler>();
                    var app = builder.Build();

                    await dispatcher.ExecuteAsync(context, new HttpConnectionDispatcherOptions(), app);
                }
            }
        }

        private class TrackingMemoryStream : MemoryStream
        {
            private readonly SemaphoreSlim _callerTracker;
            private readonly Task _waitTask;

            public TrackingMemoryStream(SemaphoreSlim callerTracker, Task waitTask)
            {
                _callerTracker = callerTracker;
                _waitTask = waitTask;
            }

            public override async Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
            {
                // Will return false if all available locks from semaphore are taken
                if (!_callerTracker.Wait(0))
                {
                    throw new Exception("Too many callers.");
                }

                try
                {
                    await _waitTask;

                    await base.CopyToAsync(destination, bufferSize, cancellationToken);
                }
                finally
                {
                    _callerTracker.Release();
                }
            }
        }

        [Fact]
        public async Task HttpContextFeatureForLongpollingWorksBetweenPolls()
        {
            using (StartLog(out var loggerFactory, LogLevel.Debug))
            {
                var manager = CreateConnectionManager(loggerFactory);
                var dispatcher = new HttpConnectionDispatcher(manager, loggerFactory);
                var connection = manager.CreateConnection();
                connection.TransportType = HttpTransportType.LongPolling;

                using (var requestBody = new MemoryStream())
                using (var responseBody = new MemoryStream())
                {
                    var context = new DefaultHttpContext();
                    context.Request.Body = requestBody;
                    context.Response.Body = responseBody;

                    var services = new ServiceCollection();
                    services.AddSingleton<HttpContextConnectionHandler>();
                    services.AddOptions();

                    // Setup state on the HttpContext
                    context.Request.Path = "/foo";
                    context.Request.Method = "GET";
                    var values = new Dictionary<string, StringValues>();
                    values["id"] = connection.ConnectionId;
                    values["another"] = "value";
                    var qs = new QueryCollection(values);
                    context.Request.Query = qs;
                    context.Request.Headers["header1"] = "h1";
                    context.Request.Headers["header2"] = "h2";
                    context.Request.Headers["header3"] = "h3";
                    context.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("claim1", "claimValue") }));
                    context.TraceIdentifier = "requestid";
                    context.Connection.Id = "connectionid";
                    context.Connection.LocalIpAddress = IPAddress.Loopback;
                    context.Connection.LocalPort = 4563;
                    context.Connection.RemoteIpAddress = IPAddress.IPv6Any;
                    context.Connection.RemotePort = 43456;

                    var builder = new ConnectionBuilder(services.BuildServiceProvider());
                    builder.UseConnectionHandler<HttpContextConnectionHandler>();
                    var app = builder.Build();

                    // Start a poll
                    var task = dispatcher.ExecuteAsync(context, new HttpConnectionDispatcherOptions(), app);

                    // Send to the application
                    var buffer = Encoding.UTF8.GetBytes("Hello World");
                    await connection.Application.Output.WriteAsync(buffer);

                    // The poll request should end
                    await task;

                    // Make sure the actual response isn't affected
                    Assert.Equal("application/octet-stream", context.Response.ContentType);

                    // Now do a new send again without the poll (that request should have ended)
                    await connection.Application.Output.WriteAsync(buffer);

                    connection.Application.Output.Complete();

                    // Wait for the endpoint to end
                    await connection.ApplicationTask;

                    var connectionHttpContext = connection.GetHttpContext();
                    Assert.NotNull(connectionHttpContext);

                    Assert.Equal(2, connectionHttpContext.Request.Query.Count);
                    Assert.Equal(connection.ConnectionId, connectionHttpContext.Request.Query["id"]);
                    Assert.Equal("value", connectionHttpContext.Request.Query["another"]);

                    Assert.Equal(3, connectionHttpContext.Request.Headers.Count);
                    Assert.Equal("h1", connectionHttpContext.Request.Headers["header1"]);
                    Assert.Equal("h2", connectionHttpContext.Request.Headers["header2"]);
                    Assert.Equal("h3", connectionHttpContext.Request.Headers["header3"]);
                    Assert.Equal("requestid", connectionHttpContext.TraceIdentifier);
                    Assert.Equal("claimValue", connectionHttpContext.User.Claims.FirstOrDefault().Value);
                    Assert.Equal("connectionid", connectionHttpContext.Connection.Id);
                    Assert.Equal(IPAddress.Loopback, connectionHttpContext.Connection.LocalIpAddress);
                    Assert.Equal(4563, connectionHttpContext.Connection.LocalPort);
                    Assert.Equal(IPAddress.IPv6Any, connectionHttpContext.Connection.RemoteIpAddress);
                    Assert.Equal(43456, connectionHttpContext.Connection.RemotePort);
                    Assert.NotNull(connectionHttpContext.RequestServices);
                    Assert.Equal(Stream.Null, connectionHttpContext.Response.Body);
                    Assert.NotNull(connectionHttpContext.Response.Headers);
                    Assert.Equal("application/xml", connectionHttpContext.Response.ContentType);
                }
            }
        }

        [Theory]
        [InlineData(HttpTransportType.ServerSentEvents)]
        [InlineData(HttpTransportType.LongPolling)]
        public async Task EndpointsThatRequireConnectionId400WhenNoConnectionIdProvided(HttpTransportType transportType)
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
                    services.AddSingleton<TestConnectionHandler>();
                    context.Request.Path = "/foo";
                    context.Request.Method = "GET";

                    SetTransport(context, transportType);

                    var builder = new ConnectionBuilder(services.BuildServiceProvider());
                    builder.UseConnectionHandler<TestConnectionHandler>();
                    var app = builder.Build();
                    await dispatcher.ExecuteAsync(context, new HttpConnectionDispatcherOptions(), app);

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
                    services.AddSingleton<TestConnectionHandler>();
                    context.Request.Path = "/foo";
                    context.Request.Method = "POST";

                    var builder = new ConnectionBuilder(services.BuildServiceProvider());
                    builder.UseConnectionHandler<TestConnectionHandler>();
                    var app = builder.Build();
                    await dispatcher.ExecuteAsync(context, new HttpConnectionDispatcherOptions(), app);

                    Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
                    await strm.FlushAsync();
                    Assert.Equal("Connection ID required", Encoding.UTF8.GetString(strm.ToArray()));
                }
            }
        }

        [Theory]
        [InlineData(HttpTransportType.LongPolling, 204)]
        [InlineData(HttpTransportType.WebSockets, 404)]
        [InlineData(HttpTransportType.ServerSentEvents, 404)]
        public async Task EndPointThatOnlySupportsLongPollingRejectsOtherTransports(HttpTransportType transportType, int status)
        {
            using (StartLog(out var loggerFactory, LogLevel.Debug))
            {
                await CheckTransportSupported(HttpTransportType.LongPolling, transportType, status, loggerFactory);
            }
        }

        [Theory]
        [InlineData(HttpTransportType.ServerSentEvents, 200)]
        [InlineData(HttpTransportType.WebSockets, 404)]
        [InlineData(HttpTransportType.LongPolling, 404)]
        public async Task EndPointThatOnlySupportsSSERejectsOtherTransports(HttpTransportType transportType, int status)
        {
            using (StartLog(out var loggerFactory, LogLevel.Debug))
            {
                await CheckTransportSupported(HttpTransportType.ServerSentEvents, transportType, status, loggerFactory);
            }
        }

        [Theory]
        [InlineData(HttpTransportType.WebSockets, 200)]
        [InlineData(HttpTransportType.ServerSentEvents, 404)]
        [InlineData(HttpTransportType.LongPolling, 404)]
        public async Task EndPointThatOnlySupportsWebSockesRejectsOtherTransports(HttpTransportType transportType, int status)
        {
            using (StartLog(out var loggerFactory, LogLevel.Debug))
            {
                await CheckTransportSupported(HttpTransportType.WebSockets, transportType, status, loggerFactory);
            }
        }

        [Theory]
        [InlineData(HttpTransportType.LongPolling, 404)]
        public async Task EndPointThatOnlySupportsWebSocketsAndSSERejectsLongPolling(HttpTransportType transportType, int status)
        {
            using (StartLog(out var loggerFactory, LogLevel.Debug))
            {
                await CheckTransportSupported(HttpTransportType.WebSockets | HttpTransportType.ServerSentEvents, transportType, status, loggerFactory);
            }
        }

        [Fact]
        public async Task CompletedEndPointEndsConnection()
        {
            using (StartLog(out var loggerFactory, LogLevel.Debug))
            {
                var manager = CreateConnectionManager(loggerFactory);
                var connection = manager.CreateConnection();
                connection.TransportType = HttpTransportType.ServerSentEvents;

                var dispatcher = new HttpConnectionDispatcher(manager, loggerFactory);

                var context = MakeRequest("/foo", connection);
                SetTransport(context, HttpTransportType.ServerSentEvents);

                var services = new ServiceCollection();
                services.AddSingleton<ImmediatelyCompleteConnectionHandler>();
                var builder = new ConnectionBuilder(services.BuildServiceProvider());
                builder.UseConnectionHandler<ImmediatelyCompleteConnectionHandler>();
                var app = builder.Build();
                await dispatcher.ExecuteAsync(context, new HttpConnectionDispatcherOptions(), app);

                Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);

                var exists = manager.TryGetConnection(connection.ConnectionId, out _);
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
                connection.TransportType = HttpTransportType.ServerSentEvents;

                var dispatcher = new HttpConnectionDispatcher(manager, loggerFactory);
                var context = MakeRequest("/foo", connection);
                SetTransport(context, HttpTransportType.ServerSentEvents);

                var services = new ServiceCollection();
                services.AddSingleton<SynchronusExceptionConnectionHandler>();
                var builder = new ConnectionBuilder(services.BuildServiceProvider());
                builder.UseConnectionHandler<SynchronusExceptionConnectionHandler>();
                var app = builder.Build();
                await dispatcher.ExecuteAsync(context, new HttpConnectionDispatcherOptions(), app);

                Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);

                var exists = manager.TryGetConnection(connection.ConnectionId, out _);
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
                connection.TransportType = HttpTransportType.LongPolling;

                var dispatcher = new HttpConnectionDispatcher(manager, loggerFactory);

                var context = MakeRequest("/foo", connection);

                var services = new ServiceCollection();
                services.AddSingleton<ImmediatelyCompleteConnectionHandler>();
                var builder = new ConnectionBuilder(services.BuildServiceProvider());
                builder.UseConnectionHandler<ImmediatelyCompleteConnectionHandler>();
                var app = builder.Build();
                await dispatcher.ExecuteAsync(context, new HttpConnectionDispatcherOptions(), app);

                Assert.Equal(StatusCodes.Status204NoContent, context.Response.StatusCode);

                var exists = manager.TryGetConnection(connection.ConnectionId, out _);
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
                connection.TransportType = HttpTransportType.LongPolling;

                var dispatcher = new HttpConnectionDispatcher(manager, loggerFactory);

                var context = MakeRequest("/foo", connection);

                var services = new ServiceCollection();
                services.AddSingleton<TestConnectionHandler>();
                var builder = new ConnectionBuilder(services.BuildServiceProvider());
                builder.UseConnectionHandler<TestConnectionHandler>();
                var app = builder.Build();
                var options = new HttpConnectionDispatcherOptions();
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
                connection.TransportType = HttpTransportType.WebSockets;

                var dispatcher = new HttpConnectionDispatcher(manager, loggerFactory);

                var context = MakeRequest("/foo", connection);
                SetTransport(context, HttpTransportType.WebSockets);

                var services = new ServiceCollection();
                services.AddSingleton<ImmediatelyCompleteConnectionHandler>();
                var builder = new ConnectionBuilder(services.BuildServiceProvider());
                builder.UseConnectionHandler<ImmediatelyCompleteConnectionHandler>();
                var app = builder.Build();
                var options = new HttpConnectionDispatcherOptions();
                options.WebSockets.CloseTimeout = TimeSpan.FromSeconds(1);

                var task = dispatcher.ExecuteAsync(context, options, app);

                await task.OrTimeout();
            }
        }

        [Theory]
        [InlineData(HttpTransportType.WebSockets)]
        [InlineData(HttpTransportType.ServerSentEvents)]
        public async Task RequestToActiveConnectionId409ForStreamingTransports(HttpTransportType transportType)
        {
            using (StartLog(out var loggerFactory, LogLevel.Debug))
            {
                var manager = CreateConnectionManager(loggerFactory);
                var connection = manager.CreateConnection();
                connection.TransportType = transportType;

                var dispatcher = new HttpConnectionDispatcher(manager, loggerFactory);

                var context1 = MakeRequest("/foo", connection);
                var context2 = MakeRequest("/foo", connection);

                SetTransport(context1, transportType);
                SetTransport(context2, transportType);

                var services = new ServiceCollection();
                services.AddSingleton<TestConnectionHandler>();
                var builder = new ConnectionBuilder(services.BuildServiceProvider());
                builder.UseConnectionHandler<TestConnectionHandler>();
                var app = builder.Build();
                var options = new HttpConnectionDispatcherOptions();
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
                connection.TransportType = HttpTransportType.LongPolling;

                var dispatcher = new HttpConnectionDispatcher(manager, loggerFactory);

                var context1 = MakeRequest("/foo", connection);
                var context2 = MakeRequest("/foo", connection);

                var services = new ServiceCollection();
                services.AddSingleton<TestConnectionHandler>();
                var builder = new ConnectionBuilder(services.BuildServiceProvider());
                builder.UseConnectionHandler<TestConnectionHandler>();
                var app = builder.Build();
                var options = new HttpConnectionDispatcherOptions();
                var request1 = dispatcher.ExecuteAsync(context1, options, app);
                var request2 = dispatcher.ExecuteAsync(context2, options, app);

                await request1;

                Assert.Equal(StatusCodes.Status204NoContent, context1.Response.StatusCode);
                Assert.Equal(HttpConnectionStatus.Active, connection.Status);

                Assert.False(request2.IsCompleted);

                manager.CloseConnections();

                await request2;
            }
        }

        [Theory]
        [InlineData(HttpTransportType.ServerSentEvents)]
        [InlineData(HttpTransportType.LongPolling)]
        public async Task RequestToDisposedConnectionIdReturns404(HttpTransportType transportType)
        {
            using (StartLog(out var loggerFactory, LogLevel.Debug))
            {
                var manager = CreateConnectionManager(loggerFactory);
                var connection = manager.CreateConnection();
                connection.TransportType = transportType;
                connection.Status = HttpConnectionStatus.Disposed;

                var dispatcher = new HttpConnectionDispatcher(manager, loggerFactory);

                var context = MakeRequest("/foo", connection);
                SetTransport(context, transportType);

                var services = new ServiceCollection();
                services.AddSingleton<TestConnectionHandler>();
                var builder = new ConnectionBuilder(services.BuildServiceProvider());
                builder.UseConnectionHandler<TestConnectionHandler>();
                var app = builder.Build();
                var options = new HttpConnectionDispatcherOptions();
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
                connection.TransportType = HttpTransportType.LongPolling;

                var dispatcher = new HttpConnectionDispatcher(manager, loggerFactory);

                var context = MakeRequest("/foo", connection);

                var services = new ServiceCollection();
                services.AddSingleton<TestConnectionHandler>();
                var builder = new ConnectionBuilder(services.BuildServiceProvider());
                builder.UseConnectionHandler<TestConnectionHandler>();
                var app = builder.Build();
                var options = new HttpConnectionDispatcherOptions();
                var task = dispatcher.ExecuteAsync(context, options, app);

                var buffer = Encoding.UTF8.GetBytes("Hello World");

                // Write to the transport so the poll yields
                await connection.Transport.Output.WriteAsync(buffer);

                await task;

                Assert.Equal(HttpConnectionStatus.Inactive, connection.Status);
                Assert.NotNull(connection.GetHttpContext());

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
                connection.TransportType = HttpTransportType.ServerSentEvents;

                var dispatcher = new HttpConnectionDispatcher(manager, loggerFactory);

                var context = MakeRequest("/foo", connection);
                SetTransport(context, HttpTransportType.ServerSentEvents);

                var services = new ServiceCollection();
                services.AddSingleton<BlockingConnectionHandler>();
                var builder = new ConnectionBuilder(services.BuildServiceProvider());
                builder.UseConnectionHandler<BlockingConnectionHandler>();
                var app = builder.Build();
                var options = new HttpConnectionDispatcherOptions();
                var task = dispatcher.ExecuteAsync(context, options, app);

                var buffer = Encoding.UTF8.GetBytes("Hello World");

                // Write to the application
                await connection.Application.Output.WriteAsync(buffer);

                await task;

                Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
                var exists = manager.TryGetConnection(connection.ConnectionId, out _);
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
                connection.TransportType = HttpTransportType.LongPolling;

                var dispatcher = new HttpConnectionDispatcher(manager, loggerFactory);

                var context = MakeRequest("/foo", connection);

                var services = new ServiceCollection();
                services.AddSingleton<BlockingConnectionHandler>();
                var builder = new ConnectionBuilder(services.BuildServiceProvider());
                builder.UseConnectionHandler<BlockingConnectionHandler>();
                var app = builder.Build();
                var options = new HttpConnectionDispatcherOptions();
                var task = dispatcher.ExecuteAsync(context, options, app);

                var buffer = Encoding.UTF8.GetBytes("Hello World");

                // Write to the application
                await connection.Application.Output.WriteAsync(buffer);

                await task;

                Assert.Equal(StatusCodes.Status204NoContent, context.Response.StatusCode);
                var exists = manager.TryGetConnection(connection.ConnectionId, out _);
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
                connection.TransportType = HttpTransportType.LongPolling;

                var dispatcher = new HttpConnectionDispatcher(manager, loggerFactory);

                var services = new ServiceCollection();
                services.AddSingleton<TestConnectionHandler>();
                var builder = new ConnectionBuilder(services.BuildServiceProvider());
                builder.UseConnectionHandler<TestConnectionHandler>();
                var app = builder.Build();
                var options = new HttpConnectionDispatcherOptions();

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
        [InlineData(HttpTransportType.LongPolling, null)]
        [InlineData(HttpTransportType.ServerSentEvents, TransferFormat.Text)]
        [InlineData(HttpTransportType.WebSockets, TransferFormat.Binary | TransferFormat.Text)]
        public async Task TransferModeSet(HttpTransportType transportType, TransferFormat? expectedTransferFormats)
        {
            using (StartLog(out var loggerFactory, LogLevel.Debug))
            {
                var manager = CreateConnectionManager(loggerFactory);
                var connection = manager.CreateConnection();
                connection.TransportType = transportType;

                var dispatcher = new HttpConnectionDispatcher(manager, loggerFactory);

                var context = MakeRequest("/foo", connection);
                SetTransport(context, transportType);

                var services = new ServiceCollection();
                services.AddSingleton<ImmediatelyCompleteConnectionHandler>();
                var builder = new ConnectionBuilder(services.BuildServiceProvider());
                builder.UseConnectionHandler<ImmediatelyCompleteConnectionHandler>();
                var app = builder.Build();

                var options = new HttpConnectionDispatcherOptions();
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
                connection.TransportType = HttpTransportType.LongPolling;
                var dispatcher = new HttpConnectionDispatcher(manager, loggerFactory);
                var context = new DefaultHttpContext();
                var services = new ServiceCollection();
                services.AddOptions();
                services.AddSingleton<TestConnectionHandler>();
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
                builder.UseConnectionHandler<TestConnectionHandler>();
                var app = builder.Build();
                var options = new HttpConnectionDispatcherOptions();
                options.AuthorizationData.Add(new AuthorizeAttribute("test"));

                // would get stuck if EndPoint was running
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
                connection.TransportType = HttpTransportType.LongPolling;
                var dispatcher = new HttpConnectionDispatcher(manager, loggerFactory);
                var context = new DefaultHttpContext();
                var services = new ServiceCollection();
                services.AddOptions();
                services.AddSingleton<TestConnectionHandler>();
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
                builder.UseConnectionHandler<TestConnectionHandler>();
                var app = builder.Build();
                var options = new HttpConnectionDispatcherOptions();
                options.AuthorizationData.Add(new AuthorizeAttribute("test"));

                context.User = new ClaimsPrincipal(new ClaimsIdentity("authenticated"));

                // would get stuck if EndPoint was running
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
                connection.TransportType = HttpTransportType.LongPolling;
                var dispatcher = new HttpConnectionDispatcher(manager, loggerFactory);
                var context = new DefaultHttpContext();
                context.Features.Set<IHttpResponseFeature>(new ResponseFeature());
                var services = new ServiceCollection();
                services.AddOptions();
                services.AddSingleton<TestConnectionHandler>();
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
                builder.UseConnectionHandler<TestConnectionHandler>();
                var app = builder.Build();
                var options = new HttpConnectionDispatcherOptions();
                options.AuthorizationData.Add(new AuthorizeAttribute("test"));

                // "authorize" user
                context.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "name") }));

                var connectionHandlerTask = dispatcher.ExecuteAsync(context, options, app);
                await connection.Transport.Output.WriteAsync(Encoding.UTF8.GetBytes("Hello, World")).AsTask().OrTimeout();

                await connectionHandlerTask.OrTimeout();

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
                connection.TransportType = HttpTransportType.LongPolling;
                var dispatcher = new HttpConnectionDispatcher(manager, loggerFactory);
                var context = new DefaultHttpContext();
                context.Features.Set<IHttpResponseFeature>(new ResponseFeature());
                var services = new ServiceCollection();
                services.AddOptions();
                services.AddSingleton<TestConnectionHandler>();
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
                builder.UseConnectionHandler<TestConnectionHandler>();
                var app = builder.Build();
                var options = new HttpConnectionDispatcherOptions();
                options.AuthorizationData.Add(new AuthorizeAttribute("test"));
                options.AuthorizationData.Add(new AuthorizeAttribute("secondPolicy"));

                // partially "authorize" user
                context.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "name") }));

                // would get stuck if EndPoint was running
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

                var connectionHandlerTask = dispatcher.ExecuteAsync(context, options, app);
                await connection.Transport.Output.WriteAsync(Encoding.UTF8.GetBytes("Hello, World")).AsTask().OrTimeout();

                await connectionHandlerTask.OrTimeout();

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
                connection.TransportType = HttpTransportType.LongPolling;
                var dispatcher = new HttpConnectionDispatcher(manager, loggerFactory);
                var context = new DefaultHttpContext();
                context.Features.Set<IHttpResponseFeature>(new ResponseFeature());
                var services = new ServiceCollection();
                services.AddOptions();
                services.AddSingleton<TestConnectionHandler>();
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
                builder.UseConnectionHandler<TestConnectionHandler>();
                var app = builder.Build();
                var options = new HttpConnectionDispatcherOptions();
                options.AuthorizationData.Add(new AuthorizeAttribute("test"));

                // "authorize" user
                context.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "name") }));

                var connectionHandlerTask = dispatcher.ExecuteAsync(context, options, app);
                await connection.Transport.Output.WriteAsync(Encoding.UTF8.GetBytes("Hello, World")).AsTask().OrTimeout();

                await connectionHandlerTask.OrTimeout();

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
                connection.TransportType = HttpTransportType.LongPolling;
                var dispatcher = new HttpConnectionDispatcher(manager, loggerFactory);
                var context = new DefaultHttpContext();
                var services = new ServiceCollection();
                services.AddOptions();
                services.AddSingleton<TestConnectionHandler>();
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
                builder.UseConnectionHandler<TestConnectionHandler>();
                var app = builder.Build();
                var options = new HttpConnectionDispatcherOptions();
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
                connection.TransportType = HttpTransportType.LongPolling;

                var dispatcher = new HttpConnectionDispatcher(manager, loggerFactory);

                var context = MakeRequest("/foo", connection);

                var services = new ServiceCollection();
                services.AddSingleton<TestConnectionHandler>();
                var builder = new ConnectionBuilder(services.BuildServiceProvider());
                builder.UseConnectionHandler<TestConnectionHandler>();
                var app = builder.Build();
                var options = new HttpConnectionDispatcherOptions();
                options.LongPolling.PollTimeout = TimeSpan.FromMilliseconds(1); // We don't care about the poll itself

                Assert.Null(connection.Features.Get<IConnectionInherentKeepAliveFeature>());

                await dispatcher.ExecuteAsync(context, options, app).OrTimeout();

                Assert.NotNull(connection.Features.Get<IConnectionInherentKeepAliveFeature>());
                Assert.Equal(options.LongPolling.PollTimeout, connection.Features.Get<IConnectionInherentKeepAliveFeature>().KeepAliveInterval);
            }
        }

        [Theory]
        [InlineData(HttpTransportType.ServerSentEvents)]
        [InlineData(HttpTransportType.WebSockets)]
        public async Task DeleteEndpointRejectsRequestToTerminateNonLongPollingTransport(HttpTransportType transportType)
        {
            using (StartLog(out var loggerFactory, LogLevel.Debug))
            {
                var manager = CreateConnectionManager(loggerFactory);
                var connection = manager.CreateConnection();
                connection.TransportType = transportType;

                var dispatcher = new HttpConnectionDispatcher(manager, loggerFactory);

                var context = MakeRequest("/foo", connection);
                SetTransport(context, transportType);

                var serviceCollection = new ServiceCollection();
                serviceCollection.AddSingleton<TestConnectionHandler>();
                var services = serviceCollection.BuildServiceProvider();
                var builder = new ConnectionBuilder(services);
                builder.UseConnectionHandler<TestConnectionHandler>();
                var app = builder.Build();
                var options = new HttpConnectionDispatcherOptions();

                _ = dispatcher.ExecuteAsync(context, options, app).OrTimeout();

                // Issue the delete request
                var deleteContext = new DefaultHttpContext();
                deleteContext.Request.Path = "/foo";
                deleteContext.Request.QueryString = new QueryString($"?id={connection.ConnectionId}");
                deleteContext.Request.Method = "DELETE";
                var ms = new MemoryStream();
                deleteContext.Response.Body = ms;

                await dispatcher.ExecuteAsync(deleteContext, options, app).OrTimeout();

                // Verify the response from the DELETE request
                Assert.Equal(StatusCodes.Status400BadRequest, deleteContext.Response.StatusCode);
                Assert.Equal("text/plain", deleteContext.Response.ContentType);
                Assert.Equal("Cannot terminate this connection using the DELETE endpoint.", Encoding.UTF8.GetString(ms.ToArray()));
            }
        }

        [Fact]
        public async Task DeleteEndpointGracefullyTerminatesLongPolling()
        {
            using (StartLog(out var loggerFactory, LogLevel.Debug))
            {
                var manager = CreateConnectionManager(loggerFactory);
                var connection = manager.CreateConnection();
                connection.TransportType = HttpTransportType.LongPolling;

                var dispatcher = new HttpConnectionDispatcher(manager, loggerFactory);

                var context = MakeRequest("/foo", connection);

                var services = new ServiceCollection();
                services.AddSingleton<TestConnectionHandler>();
                var builder = new ConnectionBuilder(services.BuildServiceProvider());
                builder.UseConnectionHandler<TestConnectionHandler>();
                var app = builder.Build();
                var options = new HttpConnectionDispatcherOptions();

                var pollTask = dispatcher.ExecuteAsync(context, options, app);

                // Issue the delete request and make sure the poll completes
                var deleteContext = new DefaultHttpContext();
                deleteContext.Request.Path = "/foo";
                deleteContext.Request.QueryString = new QueryString($"?id={connection.ConnectionId}");
                deleteContext.Request.Method = "DELETE";

                Assert.False(pollTask.IsCompleted);

                await dispatcher.ExecuteAsync(deleteContext, options, app).OrTimeout();

                await pollTask.OrTimeout();

                // Verify that everything shuts down
                await connection.ApplicationTask.OrTimeout();
                await connection.TransportTask.OrTimeout();

                // Verify the response from the DELETE request
                Assert.Equal(StatusCodes.Status202Accepted, deleteContext.Response.StatusCode);
                Assert.Equal("text/plain", deleteContext.Response.ContentType);

                // Verify the connection was removed from the manager
                Assert.False(manager.TryGetConnection(connection.ConnectionId, out _));
            }
        }

        [Fact]
        public async Task DeleteEndpointGracefullyTerminatesLongPollingEvenWhenBetweenPolls()
        {
            using (StartLog(out var loggerFactory, LogLevel.Debug))
            {
                var manager = CreateConnectionManager(loggerFactory);
                var connection = manager.CreateConnection();
                connection.TransportType = HttpTransportType.LongPolling;

                var dispatcher = new HttpConnectionDispatcher(manager, loggerFactory);

                var context = MakeRequest("/foo", connection);

                var services = new ServiceCollection();
                services.AddSingleton<TestConnectionHandler>();
                var builder = new ConnectionBuilder(services.BuildServiceProvider());
                builder.UseConnectionHandler<TestConnectionHandler>();
                var app = builder.Build();
                var options = new HttpConnectionDispatcherOptions();
                options.LongPolling.PollTimeout = TimeSpan.FromMilliseconds(1);

                await dispatcher.ExecuteAsync(context, options, app).OrTimeout();

                // Issue the delete request and make sure the poll completes
                var deleteContext = new DefaultHttpContext();
                deleteContext.Request.Path = "/foo";
                deleteContext.Request.QueryString = new QueryString($"?id={connection.ConnectionId}");
                deleteContext.Request.Method = "DELETE";

                await dispatcher.ExecuteAsync(deleteContext, options, app).OrTimeout();

                // Verify the response from the DELETE request
                Assert.Equal(StatusCodes.Status202Accepted, deleteContext.Response.StatusCode);
                Assert.Equal("text/plain", deleteContext.Response.ContentType);

                // Verify that everything shuts down
                await connection.ApplicationTask.OrTimeout();
                await connection.TransportTask.OrTimeout();

                Assert.NotNull(connection.DisposeAndRemoveTask);

                await connection.DisposeAndRemoveTask.OrTimeout();

                // Verify the connection was removed from the manager
                Assert.False(manager.TryGetConnection(connection.ConnectionId, out _));
            }
        }

        [Fact]
        public async Task NegotiateDoesNotReturnWebSocketsWhenNotAvailable()
        {
            using (StartLog(out var loggerFactory, LogLevel.Debug))
            {
                var manager = CreateConnectionManager(loggerFactory);
                var dispatcher = new HttpConnectionDispatcher(manager, loggerFactory);
                var context = new DefaultHttpContext();
                context.Features.Set<IHttpResponseFeature>(new ResponseFeature());
                var services = new ServiceCollection();
                services.AddSingleton<TestConnectionHandler>();
                services.AddOptions();
                var ms = new MemoryStream();
                context.Request.Path = "/foo";
                context.Request.Method = "POST";
                context.Response.Body = ms;
                await dispatcher.ExecuteNegotiateAsync(context, new HttpConnectionDispatcherOptions { Transports = HttpTransportType.WebSockets });

                var negotiateResponse = JsonConvert.DeserializeObject<JObject>(Encoding.UTF8.GetString(ms.ToArray()));
                var availableTransports = (JArray)negotiateResponse["availableTransports"];

                Assert.Empty(availableTransports);
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

            protected virtual bool ShouldAccept => true;

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

        private static async Task CheckTransportSupported(HttpTransportType supportedTransports, HttpTransportType transportType, int status, ILoggerFactory loggerFactory)
        {
            var manager = CreateConnectionManager(loggerFactory);
            var connection = manager.CreateConnection();
            connection.TransportType = transportType;

            var dispatcher = new HttpConnectionDispatcher(manager, loggerFactory);
            using (var strm = new MemoryStream())
            {
                var context = new DefaultHttpContext();
                context.Features.Set<IHttpResponseFeature>(new ResponseFeature());
                context.Response.Body = strm;
                var services = new ServiceCollection();
                services.AddOptions();
                services.AddSingleton<ImmediatelyCompleteConnectionHandler>();
                SetTransport(context, transportType);
                context.Request.Path = "/foo";
                context.Request.Method = "GET";
                var values = new Dictionary<string, StringValues>();
                values["id"] = connection.ConnectionId;
                var qs = new QueryCollection(values);
                context.Request.Query = qs;

                var builder = new ConnectionBuilder(services.BuildServiceProvider());
                builder.UseConnectionHandler<ImmediatelyCompleteConnectionHandler>();
                var app = builder.Build();
                var options = new HttpConnectionDispatcherOptions();
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

        private static DefaultHttpContext MakeRequest(string path, ConnectionContext connection, string format = null)
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

        private static void SetTransport(HttpContext context, HttpTransportType transportType)
        {
            switch (transportType)
            {
                case HttpTransportType.WebSockets:
                    context.Features.Set<IHttpWebSocketFeature>(new TestWebSocketConnectionFeature());
                    break;
                case HttpTransportType.ServerSentEvents:
                    context.Request.Headers["Accept"] = "text/event-stream";
                    break;
                default:
                    break;
            }
        }

        private static HttpConnectionManager CreateConnectionManager(ILoggerFactory loggerFactory)
        {
            return new HttpConnectionManager(loggerFactory ?? new LoggerFactory(), new EmptyApplicationLifetime());
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

    public class NeverEndingConnectionHandler : ConnectionHandler
    {
        public override Task OnConnectedAsync(ConnectionContext connection)
        {
            var tcs = new TaskCompletionSource<object>();
            return tcs.Task;
        }
    }

    public class BlockingConnectionHandler : ConnectionHandler
    {
        public override Task OnConnectedAsync(ConnectionContext connection)
        {
            var result = connection.Transport.Input.ReadAsync().AsTask().Result;
            connection.Transport.Input.AdvanceTo(result.Buffer.End);
            return Task.CompletedTask;
        }
    }

    public class SynchronusExceptionConnectionHandler : ConnectionHandler
    {
        public override Task OnConnectedAsync(ConnectionContext connection)
        {
            throw new InvalidOperationException();
        }
    }

    public class ImmediatelyCompleteConnectionHandler : ConnectionHandler
    {
        public override Task OnConnectedAsync(ConnectionContext connection)
        {
            return Task.CompletedTask;
        }
    }

    public class HttpContextConnectionHandler : ConnectionHandler
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

                    // Make sure we have an http context
                    var context = connection.GetHttpContext();
                    Assert.NotNull(context);

                    // Setting the response headers should have no effect
                    context.Response.ContentType = "application/xml";

                    // Echo the results
                    await connection.Transport.Output.WriteAsync(result.Buffer.ToArray());
                }
                finally
                {
                    connection.Transport.Input.AdvanceTo(result.Buffer.End);
                }
            }
        }
    }

    public class TestConnectionHandler : ConnectionHandler
    {
        private TaskCompletionSource<object> _startedTcs = new TaskCompletionSource<object>();

        public Task Started => _startedTcs.Task;

        public override async Task OnConnectedAsync(ConnectionContext connection)
        {
            _startedTcs.TrySetResult(null);

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
