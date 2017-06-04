﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Sockets.Internal;
using Microsoft.AspNetCore.Sockets.Internal.Formatters;
using Microsoft.AspNetCore.Sockets.Transports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace Microsoft.AspNetCore.Sockets
{
    public class HttpConnectionDispatcher
    {
        private readonly ConnectionManager _manager;
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger _logger;

        public HttpConnectionDispatcher(ConnectionManager manager, ILoggerFactory loggerFactory)
        {
            _manager = manager;
            _loggerFactory = loggerFactory;
            _logger = _loggerFactory.CreateLogger<HttpConnectionDispatcher>();
        }

        public async Task ExecuteAsync(HttpContext context, HttpSocketOptions options, SocketDelegate socketDelegate)
        {
            if (!await AuthorizeHelper.AuthorizeAsync(context, options.AuthorizationPolicyNames))
            {
                return;
            }

            if (HttpMethods.IsOptions(context.Request.Method))
            {
                // OPTIONS /{path}
                await ProcessNegotiate(context, options);
            }
            else if (HttpMethods.IsPost(context.Request.Method))
            {
                // POST /{path}
                await ProcessSend(context);
            }
            else if (HttpMethods.IsGet(context.Request.Method))
            {
                // GET /{path}
                await ExecuteEndpointAsync(context, socketDelegate, options);
            }
            else
            {
                context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            }
        }

        private async Task ExecuteEndpointAsync(HttpContext context, SocketDelegate socketDelegate, HttpSocketOptions options) 
        {
            var supportedTransports = options.Transports;

            // Server sent events transport
            // GET /{path}
            // Accept: text/event-stream
            var headers = context.Request.GetTypedHeaders();
            if (headers.Accept?.Contains(new Net.Http.Headers.MediaTypeHeaderValue("text/event-stream")) == true)
            {
                // Connection must already exist
                var connection = await GetConnectionAsync(context);
                if (connection == null)
                {
                    // No such connection, GetConnection already set the response status code
                    return;
                }

                if (!await EnsureConnectionStateAsync(connection, context, TransportType.ServerSentEvents, supportedTransports))
                {
                    // Bad connection state. It's already set the response status code.
                    return;
                }

                // We only need to provide the Input channel since writing to the application is handled through /send.
                var sse = new ServerSentEventsTransport(connection.Application.Input, _loggerFactory);

                await DoPersistentConnection(socketDelegate, sse, context, connection);
            }
            else if (context.WebSockets.IsWebSocketRequest)
            {
                // Connection can be established lazily
                var connection = await GetOrCreateConnectionAsync(context);
                if (connection == null)
                {
                    // No such connection, GetOrCreateConnection already set the response status code
                    return;
                }

                if (!await EnsureConnectionStateAsync(connection, context, TransportType.WebSockets, supportedTransports))
                {
                    // Bad connection state. It's already set the response status code.
                    return;
                }

                var ws = new WebSocketsTransport(options.WebSockets, connection.Application, _loggerFactory);

                await DoPersistentConnection(socketDelegate, ws, context, connection);
            }
            else
            {
                // GET /{path} maps to long polling

                // Connection must already exist
                var connection = await GetConnectionAsync(context);
                if (connection == null)
                {
                    // No such connection, GetConnection already set the response status code
                    return;
                }

                if (!await EnsureConnectionStateAsync(connection, context, TransportType.LongPolling, supportedTransports))
                {
                    // Bad connection state. It's already set the response status code.
                    return;
                }

                try
                {
                    await connection.Lock.WaitAsync();

                    if (connection.Status == DefaultConnectionContext.ConnectionStatus.Disposed)
                    {
                        _logger.LogDebug("Connection {connectionId} was disposed,", connection.ConnectionId);

                        // The connection was disposed
                        context.Response.StatusCode = StatusCodes.Status404NotFound;
                        return;
                    }

                    if (connection.Status == DefaultConnectionContext.ConnectionStatus.Active)
                    {
                        _logger.LogDebug("Connection {connectionId} is already active via {requestId}. Cancelling previous request.", connection.ConnectionId, connection.GetHttpContext().TraceIdentifier);

                        using (connection.Cancellation)
                        {
                            // Cancel the previous request
                            connection.Cancellation.Cancel();

                            try
                            {
                                // Wait for the previous request to drain
                                await connection.TransportTask;
                            }
                            catch (OperationCanceledException)
                            {
                                // Should be a cancelled task
                            }

                            _logger.LogDebug("Previous poll cancelled for {connectionId} on {requestId}.", connection.ConnectionId, connection.GetHttpContext().TraceIdentifier);
                        }
                    }

                    // Mark the connection as active
                    connection.Status = DefaultConnectionContext.ConnectionStatus.Active;

                    // Raise OnConnected for new connections only since polls happen all the time
                    if (connection.ApplicationTask == null)
                    {
                        _logger.LogDebug("Establishing new connection: {connectionId} on {requestId}", connection.ConnectionId, connection.GetHttpContext().TraceIdentifier);

                        connection.Metadata[ConnectionMetadataNames.Transport] = TransportType.LongPolling;

                        connection.ApplicationTask = ExecuteApplication(socketDelegate, connection);
                    }
                    else
                    {
                        _logger.LogDebug("Resuming existing connection: {connectionId} on {requestId}", connection.ConnectionId, connection.GetHttpContext().TraceIdentifier);
                    }

                    var longPolling = new LongPollingTransport(connection.Application.Input, _loggerFactory);

                    connection.Cancellation = new CancellationTokenSource();

                    // REVIEW: Performance of this isn't great as this does a bunch of per request allocations
                    var tokenSource = CancellationTokenSource.CreateLinkedTokenSource(connection.Cancellation.Token, context.RequestAborted);

                    // Start the transport
                    connection.TransportTask = longPolling.ProcessRequestAsync(context, tokenSource.Token);
                }
                finally
                {
                    connection.Lock.Release();
                }

                var resultTask = await Task.WhenAny(connection.ApplicationTask, connection.TransportTask);

                var pollAgain = true;

                // If the application ended before the transport task then we need to potentially need to end the
                // connection
                if (resultTask == connection.ApplicationTask)
                {
                    // Complete the transport (notifying it of the application error if there is one)
                    connection.Transport.Output.TryComplete(connection.ApplicationTask.Exception);

                    // Wait for the transport to run
                    await connection.TransportTask;

                    // If the status code is a 204 it means we didn't write anything
                    if (context.Response.StatusCode == StatusCodes.Status204NoContent)
                    {
                        // We should be able to safely dispose because there's no more data being written
                        await _manager.DisposeAndRemoveAsync(connection);

                        // Don't poll again if we've removed the connection completely
                        pollAgain = false;
                    }
                }
                else if (resultTask.IsCanceled)
                {
                    // Don't poll if the transport task was cancelled
                    pollAgain = false;
                }

                if (pollAgain)
                {
                    // Otherwise, we update the state to inactive again and wait for the next poll
                    try
                    {
                        await connection.Lock.WaitAsync();

                        if (connection.Status == DefaultConnectionContext.ConnectionStatus.Active)
                        {
                            // Mark the connection as inactive
                            connection.LastSeenUtc = DateTime.UtcNow;

                            connection.Status = DefaultConnectionContext.ConnectionStatus.Inactive;

                            connection.Metadata[ConnectionMetadataNames.HttpContext] = null;

                            // Dispose the cancellation token
                            connection.Cancellation.Dispose();

                            connection.Cancellation = null;
                        }
                    }
                    finally
                    {
                        connection.Lock.Release();
                    }
                }
            }
        }

        private DefaultConnectionContext CreateConnection(HttpContext context)
        {
            var connection = _manager.CreateConnection();
            var format = (string)context.Request.Query[ConnectionMetadataNames.Format];
            connection.User = context.User;
            connection.Metadata[ConnectionMetadataNames.HttpContext] = context;
            connection.Metadata[ConnectionMetadataNames.Format] = string.IsNullOrEmpty(format) ? "json" : format;
            return connection;
        }

        private async Task DoPersistentConnection(SocketDelegate socketDelegate,
                                                  IHttpTransport transport,
                                                  HttpContext context,
                                                  DefaultConnectionContext connection)
        {
            try
            {
                await connection.Lock.WaitAsync();

                if (connection.Status == DefaultConnectionContext.ConnectionStatus.Disposed)
                {
                    _logger.LogDebug("Connection {connectionId} was disposed,", connection.ConnectionId);

                    // Connection was disposed
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }

                // There's already an active request
                if (connection.Status == DefaultConnectionContext.ConnectionStatus.Active)
                {
                    _logger.LogDebug("Connection {connectionId} is already active via {requestId}.", connection.ConnectionId, connection.GetHttpContext().TraceIdentifier);

                    // Reject the request with a 409 conflict
                    context.Response.StatusCode = StatusCodes.Status409Conflict;
                    return;
                }

                // Mark the connection as active
                connection.Status = DefaultConnectionContext.ConnectionStatus.Active;

                // Call into the end point passing the connection
                connection.ApplicationTask = ExecuteApplication(socketDelegate, connection);

                // Start the transport
                connection.TransportTask = transport.ProcessRequestAsync(context, context.RequestAborted);
            }
            finally
            {
                connection.Lock.Release();
            }

            // Wait for any of them to end
            await Task.WhenAny(connection.ApplicationTask, connection.TransportTask);

            await _manager.DisposeAndRemoveAsync(connection);
        }

        private async Task ExecuteApplication(SocketDelegate socketDelegate, ConnectionContext connection)
        {
            // Jump onto the thread pool thread so blocking user code doesn't block the setup of the
            // connection and transport
            await AwaitableThreadPool.Yield();

            // Running this in an async method turns sync exceptions into async ones
            await socketDelegate(connection);
        }

        private Task ProcessNegotiate(HttpContext context, HttpSocketOptions options)
        {
            // Set the allowed headers for this resource
            context.Response.Headers.AppendCommaSeparatedValues("Allow", "GET", "POST", "OPTIONS");

            context.Response.ContentType = "text/plain";

            // Establish the connection
            var connection = CreateConnection(context);

            // Get the bytes for the connection id
            var connectionIdBuffer = Encoding.UTF8.GetBytes(connection.ConnectionId);

            // Write it out to the response with the right content length
            context.Response.ContentLength = connectionIdBuffer.Length;
            return context.Response.Body.WriteAsync(connectionIdBuffer, 0, connectionIdBuffer.Length);
        }

        private async Task ProcessSend(HttpContext context)
        {
            var connection = await GetConnectionAsync(context);
            if (connection == null)
            {
                // No such connection, GetConnection already set the response status code
                return;
            }

            // Read the entire payload to a byte array for now because Pipelines and ReadOnlyBytes
            // don't play well with each other yet.
            byte[] buffer;
            using (var stream = new MemoryStream())
            {
                await context.Request.Body.CopyToAsync(stream);
                await stream.FlushAsync();
                buffer = stream.ToArray();
            }

            MessageFormat messageFormat;
            if (string.Equals(context.Request.ContentType, MessageFormatter.TextContentType, StringComparison.OrdinalIgnoreCase))
            {
                messageFormat = MessageFormat.Text;
            }
            else if (string.Equals(context.Request.ContentType, MessageFormatter.BinaryContentType, StringComparison.OrdinalIgnoreCase))
            {
                messageFormat = MessageFormat.Binary;
            }
            else
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsync($"'{context.Request.ContentType}' is not a valid Content-Type for send requests.");
                return;
            }

            var reader = new BytesReader(buffer);
            var messages = ParseSendBatch(ref reader, messageFormat);

            // REVIEW: Do we want to return a specific status code here if the connection has ended?
            _logger.LogDebug("Received batch of {count} message(s)", messages.Count);
            foreach (var message in messages)
            {
                while (!connection.Application.Output.TryWrite(message))
                {
                    if (!await connection.Application.Output.WaitToWriteAsync())
                    {
                        return;
                    }
                }
            }
        }

        private async Task<bool> EnsureConnectionStateAsync(DefaultConnectionContext connection, HttpContext context, TransportType transportType, TransportType supportedTransports)
        {
            if ((supportedTransports & transportType) == 0)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                await context.Response.WriteAsync($"{transportType} transport not supported by this end point type");
                return false;
            }

            connection.User = context.User;

            var transport = connection.Metadata.Get<TransportType?>(ConnectionMetadataNames.Transport);

            if (transport == null)
            {
                connection.Metadata[ConnectionMetadataNames.Transport] = transportType;
            }
            else if (transport != transportType)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsync("Cannot change transports mid-connection");
                return false;
            }
            return true;
        }

        private async Task<DefaultConnectionContext> GetConnectionAsync(HttpContext context)
        {
            var connectionId = context.Request.Query["id"];

            if (StringValues.IsNullOrEmpty(connectionId))
            {
                // There's no connection ID: bad request
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsync("Connection ID required");
                return null;
            }

            if (!_manager.TryGetConnection(connectionId, out var connection))
            {
                // No connection with that ID: Not Found
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                await context.Response.WriteAsync("No Connection with that ID");
                return null;
            }

            return connection;
        }

        private async Task<DefaultConnectionContext> GetOrCreateConnectionAsync(HttpContext context)
        {
            var connectionId = context.Request.Query["id"];
            DefaultConnectionContext connection;

            // There's no connection id so this is a brand new connection
            if (StringValues.IsNullOrEmpty(connectionId))
            {
                connection = CreateConnection(context);
            }
            else if (!_manager.TryGetConnection(connectionId, out connection))
            {
                // No connection with that ID: Not Found
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                await context.Response.WriteAsync("No Connection with that ID");
                return null;
            }

            return connection;
        }

        private List<Message> ParseSendBatch(ref BytesReader payload, MessageFormat messageFormat)
        {
            var messages = new List<Message>();

            if (payload.Unread.Length == 0)
            {
                return messages;
            }

            if (payload.Unread[0] != MessageFormatter.GetFormatIndicator(messageFormat))
            {
                throw new FormatException($"Format indicator '{(char)payload.Unread[0]}' does not match format determined by Content-Type '{MessageFormatter.GetContentType(messageFormat)}'");
            }

            payload.Advance(1);

            // REVIEW: This needs a little work. We could probably new up exactly the right parser, if we tinkered with the inheritance hierarchy a bit.
            var parser = new MessageParser();
            while (parser.TryParseMessage(ref payload, messageFormat, out var message))
            {
                messages.Add(message);
            }
            return messages;
        }
    }
}
