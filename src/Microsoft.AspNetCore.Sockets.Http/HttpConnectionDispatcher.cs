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
                var state = await GetConnectionAsync(context);
                if (state == null)
                {
                    // No such connection, GetConnection already set the response status code
                    return;
                }

                if (!await EnsureConnectionStateAsync(state, context, TransportType.ServerSentEvents, supportedTransports))
                {
                    // Bad connection state. It's already set the response status code.
                    return;
                }

                // We only need to provide the Input channel since writing to the application is handled through /send.
                var sse = new ServerSentEventsTransport(state.Application.Input, _loggerFactory);

                await DoPersistentConnection(socketDelegate, sse, context, state);
            }
            else if (context.WebSockets.IsWebSocketRequest)
            {
                // Connection can be established lazily
                var state = await GetOrCreateConnectionAsync(context);
                if (state == null)
                {
                    // No such connection, GetOrCreateConnection already set the response status code
                    return;
                }

                if (!await EnsureConnectionStateAsync(state, context, TransportType.WebSockets, supportedTransports))
                {
                    // Bad connection state. It's already set the response status code.
                    return;
                }

                var ws = new WebSocketsTransport(options.WebSockets, state.Application, _loggerFactory);

                await DoPersistentConnection(socketDelegate, ws, context, state);
            }
            else
            {
                // GET /{path} maps to long polling

                // Connection must already exist
                var state = await GetConnectionAsync(context);
                if (state == null)
                {
                    // No such connection, GetConnection already set the response status code
                    return;
                }

                if (!await EnsureConnectionStateAsync(state, context, TransportType.LongPolling, supportedTransports))
                {
                    // Bad connection state. It's already set the response status code.
                    return;
                }

                try
                {
                    await state.Lock.WaitAsync();

                    if (state.Status == ConnectionState.ConnectionStatus.Disposed)
                    {
                        _logger.LogDebug("Connection {connectionId} was disposed,", state.Connection.ConnectionId);

                        // The connection was disposed
                        context.Response.StatusCode = StatusCodes.Status404NotFound;
                        return;
                    }

                    if (state.Status == ConnectionState.ConnectionStatus.Active)
                    {
                        _logger.LogDebug("Connection {connectionId} is already active via {requestId}. Cancelling previous request.", state.Connection.ConnectionId, state.RequestId);

                        using (state.Cancellation)
                        {
                            // Cancel the previous request
                            state.Cancellation.Cancel();

                            try
                            {
                                // Wait for the previous request to drain
                                await state.TransportTask;
                            }
                            catch (OperationCanceledException)
                            {
                                // Should be a cancelled task
                            }

                            _logger.LogDebug("Previous poll cancelled for {connectionId} on {requestId}.", state.Connection.ConnectionId, state.RequestId);
                        }
                    }

                    // Mark the request identifier
                    state.RequestId = context.TraceIdentifier;

                    // Mark the connection as active
                    state.Status = ConnectionState.ConnectionStatus.Active;

                    // Raise OnConnected for new connections only since polls happen all the time
                    if (state.ApplicationTask == null)
                    {
                        _logger.LogDebug("Establishing new connection: {connectionId} on {requestId}", state.Connection.ConnectionId, state.RequestId);

                        state.Connection.Metadata[ConnectionMetadataNames.Transport] = TransportType.LongPolling;

                        state.ApplicationTask = ExecuteApplication(socketDelegate, state.Connection);
                    }
                    else
                    {
                        _logger.LogDebug("Resuming existing connection: {connectionId} on {requestId}", state.Connection.ConnectionId, state.RequestId);
                    }

                    var longPolling = new LongPollingTransport(state.Application.Input, _loggerFactory);

                    state.Cancellation = new CancellationTokenSource();

                    // REVIEW: Performance of this isn't great as this does a bunch of per request allocations
                    var tokenSource = CancellationTokenSource.CreateLinkedTokenSource(state.Cancellation.Token, context.RequestAborted);

                    // Start the transport
                    state.TransportTask = longPolling.ProcessRequestAsync(context, tokenSource.Token);
                }
                finally
                {
                    state.Lock.Release();
                }

                var resultTask = await Task.WhenAny(state.ApplicationTask, state.TransportTask);

                var pollAgain = true;

                // If the application ended before the transport task then we need to potentially need to end the
                // connection
                if (resultTask == state.ApplicationTask)
                {
                    // Complete the transport (notifying it of the application error if there is one)
                    state.Connection.Transport.Output.TryComplete(state.ApplicationTask.Exception);

                    // Wait for the transport to run
                    await state.TransportTask;

                    // If the status code is a 204 it means we didn't write anything
                    if (context.Response.StatusCode == StatusCodes.Status204NoContent)
                    {
                        // We should be able to safely dispose because there's no more data being written
                        await _manager.DisposeAndRemoveAsync(state);

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
                        await state.Lock.WaitAsync();

                        if (state.Status == ConnectionState.ConnectionStatus.Active)
                        {
                            // Mark the connection as inactive
                            state.LastSeenUtc = DateTime.UtcNow;

                            state.Status = ConnectionState.ConnectionStatus.Inactive;

                            state.RequestId = null;

                            // Dispose the cancellation token
                            state.Cancellation.Dispose();

                            state.Cancellation = null;
                        }
                    }
                    finally
                    {
                        state.Lock.Release();
                    }
                }
            }
        }

        private ConnectionState CreateConnection(HttpContext context)
        {
            var state = _manager.CreateConnection();
            var format = (string)context.Request.Query[ConnectionMetadataNames.Format];
            state.Connection.User = context.User;
            state.Connection.Metadata[ConnectionMetadataNames.HttpContext] = context;
            state.Connection.Metadata[ConnectionMetadataNames.Format] = string.IsNullOrEmpty(format) ? "json" : format;
            return state;
        }

        private async Task DoPersistentConnection(SocketDelegate socketDelegate,
                                                  IHttpTransport transport,
                                                  HttpContext context,
                                                  ConnectionState state)
        {
            try
            {
                await state.Lock.WaitAsync();

                if (state.Status == ConnectionState.ConnectionStatus.Disposed)
                {
                    _logger.LogDebug("Connection {connectionId} was disposed,", state.Connection.ConnectionId);

                    // Connection was disposed
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }

                // There's already an active request
                if (state.Status == ConnectionState.ConnectionStatus.Active)
                {
                    _logger.LogDebug("Connection {connectionId} is already active via {requestId}.", state.Connection.ConnectionId, state.RequestId);

                    // Reject the request with a 409 conflict
                    context.Response.StatusCode = StatusCodes.Status409Conflict;
                    return;
                }

                // Mark the connection as active
                state.Status = ConnectionState.ConnectionStatus.Active;

                // Store the request identifier
                state.RequestId = context.TraceIdentifier;

                // Call into the end point passing the connection
                state.ApplicationTask = ExecuteApplication(socketDelegate, state.Connection);

                // Start the transport
                state.TransportTask = transport.ProcessRequestAsync(context, context.RequestAborted);
            }
            finally
            {
                state.Lock.Release();
            }

            // Wait for any of them to end
            await Task.WhenAny(state.ApplicationTask, state.TransportTask);

            await _manager.DisposeAndRemoveAsync(state);
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
            var state = CreateConnection(context);

            // Get the bytes for the connection id
            var connectionIdBuffer = Encoding.UTF8.GetBytes(state.Connection.ConnectionId);

            // Write it out to the response with the right content length
            context.Response.ContentLength = connectionIdBuffer.Length;
            return context.Response.Body.WriteAsync(connectionIdBuffer, 0, connectionIdBuffer.Length);
        }

        private async Task ProcessSend(HttpContext context)
        {
            var state = await GetConnectionAsync(context);
            if (state == null)
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
                while (!state.Application.Output.TryWrite(message))
                {
                    if (!await state.Application.Output.WaitToWriteAsync())
                    {
                        return;
                    }
                }
            }
        }

        private async Task<bool> EnsureConnectionStateAsync(ConnectionState connectionState, HttpContext context, TransportType transportType, TransportType supportedTransports)
        {
            if ((supportedTransports & transportType) == 0)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                await context.Response.WriteAsync($"{transportType} transport not supported by this end point type");
                return false;
            }

            connectionState.Connection.User = context.User;

            var transport = connectionState.Connection.Metadata.Get<TransportType?>(ConnectionMetadataNames.Transport);

            if (transport == null)
            {
                connectionState.Connection.Metadata[ConnectionMetadataNames.Transport] = transportType;
            }
            else if (transport != transportType)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsync("Cannot change transports mid-connection");
                return false;
            }
            return true;
        }

        private async Task<ConnectionState> GetConnectionAsync(HttpContext context)
        {
            var connectionId = context.Request.Query["id"];
            ConnectionState connectionState;

            if (StringValues.IsNullOrEmpty(connectionId))
            {
                // There's no connection ID: bad request
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsync("Connection ID required");
                return null;
            }

            if (!_manager.TryGetConnection(connectionId, out connectionState))
            {
                // No connection with that ID: Not Found
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                await context.Response.WriteAsync("No Connection with that ID");
                return null;
            }

            return connectionState;
        }

        private async Task<ConnectionState> GetOrCreateConnectionAsync(HttpContext context)
        {
            var connectionId = context.Request.Query["id"];
            ConnectionState connectionState;

            // There's no connection id so this is a brand new connection
            if (StringValues.IsNullOrEmpty(connectionId))
            {
                connectionState = CreateConnection(context);
            }
            else if (!_manager.TryGetConnection(connectionId, out connectionState))
            {
                // No connection with that ID: Not Found
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                await context.Response.WriteAsync("No Connection with that ID");
                return null;
            }

            return connectionState;
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
