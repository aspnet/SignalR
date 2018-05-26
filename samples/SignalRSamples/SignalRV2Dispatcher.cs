using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Http.Connections.Internal;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace SignalRSamples
{

    public class SignalRV2Dispatcher : HttpConnectionDispatcher
    {
        public SignalRV2Dispatcher(HttpConnectionManager manager, ILoggerFactory loggerFactory) : base(manager, loggerFactory)
        {
        }

        protected override string GetConnectionId(HttpContext context) => context.Request.Query["connectionToken"];

        public async Task ExecuteNegotiateAsync(HttpContext context)
        {
            var logScope = new ConnectionLogScope(GetConnectionId(context));
            using (_logger.BeginScope(logScope))
            {
                /*if (!await AuthorizeHelper.AuthorizeAsync(context, options.AuthorizationData))
                {
                    return;
                }*/

                await ProcessNegotiate(context, logScope);
            }
        }

        protected override async Task<bool> EnsureConnectionStateAsync(HttpConnectionContext connection, HttpContext context, HttpTransportType transportType, HttpTransportType supportedTransports, ConnectionLogScope logScope, HttpConnectionDispatcherOptions options)
        {
            if (!await base.EnsureConnectionStateAsync(connection, context, transportType, supportedTransports, logScope, options))
            {
                return false;
            }

            var connectionData = context.Request.Query["connectionData"];

            connection.Items["connectionData"] = connectionData;

            return true;
        }

        public async Task ExecuteConnectAsync(HttpContext context, HttpConnectionDispatcherOptions options, ConnectionDelegate connectionDelegate)
        {
            var logScope = new ConnectionLogScope(GetConnectionId(context));
            using (_logger.BeginScope(logScope))
            {
                /*if (!await AuthorizeHelper.AuthorizeAsync(context, options.AuthorizationData))
                {
                    return;
                }*/

                var transport = context.Request.Query["transport"];

                if (string.Equals(transport, "websockets", StringComparison.OrdinalIgnoreCase))
                {
                    await ExecuteWebSockets(context, connectionDelegate, options, logScope);
                }
                else if (string.Equals(transport, "serverSentEvents", StringComparison.OrdinalIgnoreCase))
                {
                    await ExecuteServerSentEvents(context, connectionDelegate, options, logScope);
                }
                else if (string.Equals(transport, "longPolling", StringComparison.OrdinalIgnoreCase))
                {
                    await ExecuteLongPolling(context, connectionDelegate, options, logScope);
                }

                // No forever frame...
            }
        }

        public Task ExecuteAbortAsync(HttpContext context)
        {
            return ProcessDeleteAsync(context);
        }

        public Task ExecuteStartAsync(HttpContext context)
        {
            return context.Response.WriteAsync("{\"Response\":\"started\"}");
        }

        public Task ExecutePingAsync(HttpContext context)
        {
            return context.Response.WriteAsync("{\"Response\":\"pong\"}");
        }

        public Task ExecuteReconnectAsync(HttpContext context, HttpConnectionDispatcherOptions options, ConnectionDelegate connectionDelegate)
        {
            return Task.CompletedTask;
        }

        public async Task ExecuteSendAsync(HttpContext context)
        {
            if (!context.Request.HasFormContentType)
            {
                // Some clients don't set this
                context.Request.ContentType = "application/x-www-form-urlencoded; charset=utf-8";
            }

            var connection = await GetConnectionAsync(context);
            if (connection == null)
            {
                // No such connection, GetConnection already set the response status code
                return;
            }

            context.Response.ContentType = "text/plain";

            if (connection.TransportType == HttpTransportType.WebSockets)
            {
                // Log.PostNotAllowedForWebSockets(_logger);
                context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
                await context.Response.WriteAsync("POST requests are not allowed for WebSocket connections.");
                return;
            }

            await connection.WriteLock.WaitAsync();

            try
            {
                if (connection.Status == HttpConnectionStatus.Disposed)
                {
                    // The connection was disposed
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }

                try
                {
                    var form = await context.Request.ReadFormAsync();
                    var data = form["data"];

                    if (string.IsNullOrEmpty(data))
                    {
                        return;
                    }

                    await connection.Application.Output.WriteAsync(Encoding.UTF8.GetBytes(data));
                }
                catch (InvalidOperationException)
                {
                    // PipeWriter will throw an error if it is written to while dispose is in progress and the writer has been completed
                    // Dispose isn't taking WriteLock because it could be held because of backpressure, and calling CancelPendingFlush
                    // then taking the lock introduces a race condition that could lead to a deadlock
                    // Log.ConnectionDisposedWhileWriteInProgress(_logger, connection.ConnectionId, ex);

                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }
                catch (OperationCanceledException)
                {
                    // CancelPendingFlush has canceled pending writes caused by backpresure
                    // Log.ConnectionDisposed(_logger, connection.ConnectionId);

                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }
                catch (IOException)
                {
                    // Can occur when the HTTP request is canceled by the client
                    // Log.FailedToReadHttpRequestBody(_logger, connection.ConnectionId, ex);

                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }
            }
            finally
            {
                connection.WriteLock.Release();
            }
        }

        private Task ProcessNegotiate(HttpContext context, ConnectionLogScope logScope)
        {
            context.Response.ContentType = "application/json";

            // Establish the connection
            var connection = _manager.CreateConnection();

            // Set the Connection ID on the logging scope so that logs from now on will have the
            // Connection ID metadata set.
            logScope.ConnectionId = connection.ConnectionId;

            // Get the bytes for the connection id
            var negotiateResponseBuffer = Encoding.UTF8.GetBytes(GetNegotiatePayload(connection.ConnectionId, context));

            // Write it out to the response with the right content length
            context.Response.ContentLength = negotiateResponseBuffer.Length;
            return context.Response.Body.WriteAsync(negotiateResponseBuffer, 0, negotiateResponseBuffer.Length);
        }

        private static string GetNegotiatePayload(string connectionId, HttpContext context)
        {
            var sb = new StringBuilder();
            using (var jsonWriter = new JsonTextWriter(new StringWriter(sb)))
            {
                jsonWriter.WriteStartObject();
                jsonWriter.WritePropertyName("ConnectionToken");
                jsonWriter.WriteValue(connectionId);
                jsonWriter.WritePropertyName("ConnectionId");
                jsonWriter.WriteValue(connectionId);
                jsonWriter.WritePropertyName("KeepAliveTimeout");
                jsonWriter.WriteValue("100000.0");
                jsonWriter.WritePropertyName("DisconnectTimeout");
                jsonWriter.WriteValue("5.0");
                jsonWriter.WritePropertyName("TryWebSockets");
                jsonWriter.WriteValue(ServerHasWebSockets(context.Features).ToString());
                jsonWriter.WritePropertyName("ProtocolVersion");
                jsonWriter.WriteValue(context.Request.Query["clientProtocol"]);
                jsonWriter.WritePropertyName("TransportConnectTimeout");
                jsonWriter.WriteValue("30");
                jsonWriter.WritePropertyName("LongPollDelay");
                jsonWriter.WriteValue("0.0");
                jsonWriter.WriteEndObject();
            }

            return sb.ToString();
        }

        private static bool ServerHasWebSockets(IFeatureCollection features)
        {
            return features.Get<IHttpWebSocketFeature>() != null;
        }
    }
}