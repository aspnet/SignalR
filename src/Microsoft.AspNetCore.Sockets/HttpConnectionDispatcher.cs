// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.IO.Pipelines;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Sockets.Internal;
using Microsoft.AspNetCore.Sockets.Transports;
using Microsoft.Extensions.DependencyInjection;
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

        public async Task ExecuteAsync<TEndPoint>(string path, HttpContext context) where TEndPoint : EndPoint
        {
            // Get the end point mapped to this http connection
            var endpoint = (EndPoint)context.RequestServices.GetRequiredService<TEndPoint>();

            if (context.Request.Path.StartsWithSegments(path + "/negotiate"))
            {
                await ProcessNegotiate(context);
            }
            else
            {
                await ExecuteEndpointAsync(path, context, endpoint);
            }
        }

        private async Task ExecuteEndpointAsync(string path, HttpContext context, EndPoint endpoint)
        {
            var isWebSockets = context.Request.Path.StartsWithSegments(path + "/ws");

            // Check if there's a connection ID
            var connectionId = context.Request.Query["id"];
            ConnectionState connectionState;

            if (StringValues.IsNullOrEmpty(connectionId))
            {
                // If there isn't, WebSockets can create an "unnamed" connection (because it is full duplex).
                if (isWebSockets)
                {
                    // Create the connection and return it
                    connectionState = CreateConnection(context);
                }
                else
                {
                    // No existing connection, which is required for the current transport.
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await context.Response.WriteAsync("Connection ID required");
                    return;
                }
            }
            // There's a connection ID! So look it up
            else if (!_manager.TryGetConnection(connectionId, out connectionState))
            {
                // It wasn't found. Boo! Bad client.
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                await context.Response.WriteAsync("No Connection with that ID");
                return;
            }

            if (isWebSockets)
            {
                var transport = new WebSocketsTransport(connectionState.Application, _loggerFactory);
                await ExecuteTransportAsync(context, endpoint, connectionState, transport);
            }
            else if (context.Request.Path.StartsWithSegments(path + "/sse"))
            {
                // We only need to provide the Input channel since writing to the application is handled through /send.
                var transport = new ServerSentEventsTransport(connectionState.Application.Input, _loggerFactory);
                await ExecuteTransportAsync(context, endpoint, connectionState, transport);
            }
            else if (context.Request.Path.StartsWithSegments(path + "/poll"))
            {
                await ExecuteLongPollingAsync(context, endpoint, connectionState);
            }
            else if (context.Request.Path.StartsWithSegments(path + "/send"))
            {
                await ExecuteSendAsync(context, connectionState);
            }
        }

        private async Task ExecuteTransportAsync(HttpContext context, EndPoint endpoint, ConnectionState connectionState, IHttpTransport transport)
        {
            // Set up connection state
            if (!EnsureConnectionState(connectionState, context, transport.Name))
            {
                // Changed transports mid-connection. Bad client!
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsync("Cannot change transports mid-connection");
                return;
            }

            await DoPersistentConnection(endpoint, transport, context, connectionState);
            _manager.RemoveConnection(connectionState.Connection.ConnectionId);
        }

        private async Task ExecuteLongPollingAsync(HttpContext context, EndPoint endpoint, ConnectionState connectionState)
        {
            // Mark the connection as active
            connectionState.Active = true;

            var longPolling = new LongPollingTransport(connectionState.Application.Input, _loggerFactory);

            // Set up connection state
            if (!EnsureConnectionState(connectionState, context, longPolling.Name))
            {
                // Changed transports mid-connection. Bad client!
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsync("Cannot change transports mid-connection");
                return;
            }

            // Start the transport
            var transportTask = longPolling.ProcessRequestAsync(context);

            // Raise OnConnected for new connections only since polls happen all the time
            var endpointTask = connectionState.Connection.Metadata.Get<Task>("endpoint");
            if (endpointTask == null)
            {
                _logger.LogDebug("Establishing new Long Polling connection: {0}", connectionState.Connection.ConnectionId);

                // REVIEW: This is super gross, this all needs to be cleaned up...
                connectionState.Close = async () =>
                {
                    // Close the end point's connection
                    connectionState.Connection.Dispose();

                    try
                    {
                        await endpointTask;
                    }
                    catch
                    {
                        // possibly invoked on a ThreadPool thread
                    }
                };

                endpointTask = endpoint.OnConnectedAsync(connectionState.Connection);
                connectionState.Connection.Metadata["endpoint"] = endpointTask;
            }
            else
            {
                _logger.LogDebug("Resuming existing Long Polling connection: {0}", connectionState.Connection.ConnectionId);
            }

            var resultTask = await Task.WhenAny(endpointTask, transportTask);

            if (resultTask == endpointTask)
            {
                // Notify the long polling transport to end
                if (endpointTask.IsFaulted)
                {
                    connectionState.Connection.Transport.Output.TryComplete(endpointTask.Exception.InnerException);
                }

                connectionState.Connection.Dispose();

                await transportTask;
            }

            // Mark the connection as inactive
            connectionState.LastSeenUtc = DateTime.UtcNow;
            connectionState.Active = false;
        }

        private ConnectionState CreateConnection(HttpContext context)
        {
            var format =
                string.Equals(context.Request.Query["format"], "binary", StringComparison.OrdinalIgnoreCase)
                    ? Format.Binary
                    : Format.Text;

            var state = _manager.CreateConnection();
            state.Connection.User = context.User;

            // TODO: this is wrong. + how does the user add their own metadata based on HttpContext
            var formatType = (string)context.Request.Query["formatType"];
            state.Connection.Metadata["formatType"] = string.IsNullOrEmpty(formatType) ? "json" : formatType;
            return state;
        }

        private static async Task DoPersistentConnection(EndPoint endpoint,
                                                         IHttpTransport transport,
                                                         HttpContext context,
                                                         ConnectionState state)
        {
            // Start the transport
            var transportTask = transport.ProcessRequestAsync(context);

            // Call into the end point passing the connection
            var endpointTask = endpoint.OnConnectedAsync(state.Connection);

            // Wait for any of them to end
            await Task.WhenAny(endpointTask, transportTask);

            // Kill the channel
            state.Dispose();

            // Wait for both
            await Task.WhenAll(endpointTask, transportTask);
        }

        private Task ProcessNegotiate(HttpContext context)
        {
            // Establish the connection
            var state = CreateConnection(context);

            // Get the bytes for the connection id
            var connectionIdBuffer = Encoding.UTF8.GetBytes(state.Connection.ConnectionId);

            // Write it out to the response with the right content length
            context.Response.ContentLength = connectionIdBuffer.Length;
            return context.Response.Body.WriteAsync(connectionIdBuffer, 0, connectionIdBuffer.Length);
        }

        private async Task ExecuteSendAsync(HttpContext context, ConnectionState connectionState)
        {
            // Verify that we have a transport that supports '/send'
            if (string.Equals(connectionState.Connection.Metadata.Get<string>("transport"), WebSocketsTransport.TransportName))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsync("Cannot use '/send' to send messages to a WebSockets connection");
                return;
            }

            // Collect the message and write it to the channel
            // TODO: Need to use some kind of pooled memory here.
            byte[] buffer;
            using (var stream = new MemoryStream())
            {
                await context.Request.Body.CopyToAsync(stream);
                buffer = stream.ToArray();
            }

            var format =
                string.Equals(context.Request.Query["format"], "binary", StringComparison.OrdinalIgnoreCase)
                    ? Format.Binary
                    : Format.Text;

            var message = new Message(
                ReadableBuffer.Create(buffer).Preserve(),
                format,
                endOfMessage: true);

            // REVIEW: Do we want to return a specific status code here if the connection has ended?
            while (await connectionState.Application.Output.WaitToWriteAsync())
            {
                if (connectionState.Application.Output.TryWrite(message))
                {
                    break;
                }
            }
        }

        private bool EnsureConnectionState(ConnectionState connectionState, HttpContext context, string transportName)
        {
            connectionState.Connection.User = context.User;

            var transport = connectionState.Connection.Metadata.Get<string>("transport");
            if (string.IsNullOrEmpty(transport))
            {
                connectionState.Connection.Metadata["transport"] = transportName;
            }
            else if (!string.Equals(transport, transportName, StringComparison.Ordinal))
            {
                return false;
            }
            return true;
        }
    }
}
