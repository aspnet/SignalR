// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Sockets.Internal.Transports
{
    public class WebSocketsTransport : IHttpTransport
    {
        private readonly WebSocketOptions _options;
        private readonly ILogger _logger;
        private readonly IDuplexPipe _application;
        private readonly DefaultConnectionContext _connection;
        private volatile bool _aborted;

        public WebSocketsTransport(WebSocketOptions options, IDuplexPipe application, DefaultConnectionContext connection, ILoggerFactory loggerFactory)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (application == null)
            {
                throw new ArgumentNullException(nameof(application));
            }

            if (loggerFactory == null)
            {
                throw new ArgumentNullException(nameof(loggerFactory));
            }

            _options = options;
            _application = application;
            _connection = connection;
            _logger = loggerFactory.CreateLogger<WebSocketsTransport>();
        }

        public async Task ProcessRequestAsync(HttpContext context, CancellationToken token)
        {
            Debug.Assert(context.WebSockets.IsWebSocketRequest, "Not a websocket request");

            using (var ws = await context.WebSockets.AcceptWebSocketAsync(_options.SubProtocol))
            {
                _logger.SocketOpened();

                try
                {
                    await ProcessSocketAsync(ws);
                }
                finally
                {
                    _logger.SocketClosed();
                }
            }
        }

        public async Task ProcessSocketAsync(WebSocket socket)
        {
            // Begin sending and receiving. Receiving must be started first because ExecuteAsync enables SendAsync.
            var receiving = StartReceiving(socket);
            var sending = StartSending(socket);

            // Wait for send or receive to complete
            var trigger = await Task.WhenAny(receiving, sending);

            if (trigger == receiving)
            {
                _logger.WaitingForSend();

                // We're waiting for the application to finish and there are 2 things it could be doing
                // 1. Waiting for application data
                // 2. Waiting for a websocket send to complete

                // Cancel the application so that ReadAsync yields
                _application.Input.CancelPendingRead();

                using (var delayCts = new CancellationTokenSource())
                {
                    var resultTask = await Task.WhenAny(sending, Task.Delay(_options.CloseTimeout, delayCts.Token));

                    if (resultTask != sending)
                    {
                        // We timed out so now we're in ungraceful shutdown mode
                        _logger.CloseTimedOut();

                        // Abort the websocket if we're stuck in a pending send to the client
                        _aborted = true;

                        socket.Abort();
                    }
                    else
                    {
                        delayCts.Cancel();
                    }
                }
            }
            else
            {
                _logger.WaitingForClose();

                // We're waiting on the websocket to close and there are 2 things it could be doing
                // 1. Waiting for websocket data
                // 2. Waiting on a flush to complete (backpressure being applied)

                using (var delayCts = new CancellationTokenSource())
                {
                    var resultTask = await Task.WhenAny(receiving, Task.Delay(_options.CloseTimeout, delayCts.Token));

                    if (resultTask != receiving)
                    {
                        // Abort the websocket if we're stuck in a pending receive from the client
                        _aborted = true;

                        socket.Abort();

                        // Cancel any pending flush so that we can quit
                        _application.Output.CancelPendingFlush();
                    }
                    else
                    {
                        delayCts.Cancel();
                    }
                }
            }
        }

        private async Task StartReceiving(WebSocket socket)
        {
            try
            {
                while (true)
                {
                    var memory = _application.Output.GetMemory();

#if NETCOREAPP2_1
                    var receiveResult = await socket.ReceiveAsync(memory, CancellationToken.None);
#else
                    var isArray = memory.TryGetArray(out var arraySegment);
                    Debug.Assert(isArray);

                    // Exceptions are handled above where the send and receive tasks are being run.
                    var receiveResult = await socket.ReceiveAsync(arraySegment, CancellationToken.None);
#endif
                    if (receiveResult.MessageType == WebSocketMessageType.Close)
                    {
                        return;
                    }

                    _logger.MessageReceived(receiveResult.MessageType, receiveResult.Count, receiveResult.EndOfMessage);

                    _application.Output.Advance(receiveResult.Count);

                    if (receiveResult.EndOfMessage)
                    {
                        var flushResult = await _application.Output.FlushAsync();

                        // We canceled in the middle of applying back pressure
                        // or if the consumer is done
                        if (flushResult.IsCancelled || flushResult.IsCompleted)
                        {
                            break;
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Ignore aborts, don't treat them like transport errors
            }
            catch (Exception ex)
            {
                if (!_aborted)
                {
                    _application.Output.Complete(ex);

                    // We re-throw here so we can communicate that there was an error when sending
                    // the close frame
                    throw;
                }
            }
            finally
            {
                // We're done writing
                _application.Output.Complete();
            }
        }

        private async Task StartSending(WebSocket socket)
        {
            Exception error = null;

            try
            {
                while (true)
                {
                    var result = await _application.Input.ReadAsync();
                    var buffer = result.Buffer;

                    // Get a frame from the application

                    try
                    {
                        if (result.IsCancelled)
                        {
                            break;
                        }

                        if (!buffer.IsEmpty)
                        {
                            try
                            {
                                _logger.SendPayload(buffer.Length);

                                var webSocketMessageType = (_connection.TransferMode == TransferMode.Binary
                                    ? WebSocketMessageType.Binary
                                    : WebSocketMessageType.Text);

                                if (WebSocketCanSend(socket))
                                {
                                    await socket.SendAsync(buffer, webSocketMessageType);
                                }
                                else
                                {
                                    break;
                                }
                            }
                            catch (Exception ex)
                            {
                                if (!_aborted)
                                {
                                    _logger.ErrorWritingFrame(ex);
                                }
                                break;
                            }
                        }
                        else if (result.IsCompleted)
                        {
                            break;
                        }
                    }
                    finally
                    {
                        _application.Input.AdvanceTo(buffer.End);
                    }
                }
            }
            catch (Exception ex)
            {
                error = ex;
            }
            finally
            {
                // Send the close frame before calling into user code
                if (WebSocketCanSend(socket))
                {
                    // We're done sending, send the close frame to the client if the websocket is still open
                    await socket.CloseOutputAsync(error != null ? WebSocketCloseStatus.InternalServerError : WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                }

                _application.Input.Complete();
            }

        }

        private static bool WebSocketCanSend(WebSocket ws)
        {
            return !(ws.State == WebSocketState.Aborted ||
                   ws.State == WebSocketState.Closed ||
                   ws.State == WebSocketState.CloseSent);
        }

        private static class Log
        {
            private static readonly Action<ILogger, Exception> _socketOpened =
                LoggerMessage.Define(LogLevel.Information, new EventId(1, nameof(SocketOpened)), "Socket opened.");

            private static readonly Action<ILogger, Exception> _socketClosed =
                LoggerMessage.Define(LogLevel.Information, new EventId(2, nameof(SocketClosed)), "Socket closed.");

            private static readonly Action<ILogger, WebSocketCloseStatus?, string, Exception> _clientClosed =
                LoggerMessage.Define<WebSocketCloseStatus?, string>(LogLevel.Debug, new EventId(3, nameof(ClientClosed)), "Client closed connection with status code '{status}' ({description}). Signaling end-of-input to application.");

            private static readonly Action<ILogger, Exception> _waitingForSend =
                LoggerMessage.Define(LogLevel.Debug, new EventId(4, nameof(WaitingForSend)), "Waiting for the application to finish sending data.");

            private static readonly Action<ILogger, Exception> _failedSending =
                LoggerMessage.Define(LogLevel.Debug, new EventId(5, nameof(FailedSending)), "Application failed during sending. Sending InternalServerError close frame.");

            private static readonly Action<ILogger, Exception> _finishedSending =
                LoggerMessage.Define(LogLevel.Debug, new EventId(6, nameof(FinishedSending)), "Application finished sending. Sending close frame.");

            private static readonly Action<ILogger, Exception> _waitingForClose =
                LoggerMessage.Define(LogLevel.Debug, new EventId(7, nameof(WaitingForClose)), "Waiting for the client to close the socket.");

            private static readonly Action<ILogger, Exception> _closeTimedOut =
                LoggerMessage.Define(LogLevel.Debug, new EventId(8, nameof(CloseTimedOut)), "Timed out waiting for client to send the close frame, aborting the connection.");

            private static readonly Action<ILogger, WebSocketMessageType, int, bool, Exception> _messageReceived =
                LoggerMessage.Define<WebSocketMessageType, int, bool>(LogLevel.Debug, new EventId(9, nameof(MessageReceived)), "Message received. Type: {messageType}, size: {size}, EndOfMessage: {endOfMessage}.");

            private static readonly Action<ILogger, int, Exception> _messageToApplication =
                LoggerMessage.Define<int>(LogLevel.Debug, new EventId(10, nameof(MessageToApplication)), "Passing message to application. Payload size: {size}.");

            private static readonly Action<ILogger, long, Exception> _sendPayload =
                LoggerMessage.Define<long>(LogLevel.Debug, new EventId(11, nameof(SendPayload)), "Sending payload: {size} bytes.");

            private static readonly Action<ILogger, Exception> _errorWritingFrame =
                LoggerMessage.Define(LogLevel.Error, new EventId(12, nameof(ErrorWritingFrame)), "Error writing frame.");

            private static readonly Action<ILogger, Exception> _sendFailed =
                LoggerMessage.Define(LogLevel.Trace, new EventId(13, nameof(SendFailed)), "Socket failed to send.");

        public static void SocketOpened(this ILogger logger)
        {
            _socketOpened(logger, null);
        }

        public static void SocketClosed(this ILogger logger)
        {
            _socketClosed(logger, null);
        }

        public static void ClientClosed(this ILogger logger, WebSocketCloseStatus? closeStatus, string closeDescription)
        {
            _clientClosed(logger, closeStatus, closeDescription, null);
        }

        public static void WaitingForSend(this ILogger logger)
        {
            _waitingForSend(logger, null);
        }

        public static void FailedSending(this ILogger logger)
        {
            _failedSending(logger, null);
        }

        public static void FinishedSending(this ILogger logger)
        {
            _finishedSending(logger, null);
        }

        public static void WaitingForClose(this ILogger logger)
        {
            _waitingForClose(logger, null);
        }

        public static void CloseTimedOut(this ILogger logger)
        {
            _closeTimedOut(logger, null);
        }

        public static void MessageReceived(this ILogger logger, WebSocketMessageType type, int size, bool endOfMessage)
        {
            _messageReceived(logger, type, size, endOfMessage, null);
        }

        public static void MessageToApplication(this ILogger logger, int size)
        {
            _messageToApplication(logger, size, null);
        }

        public static void SendPayload(this ILogger logger, long size)
        {
            _sendPayload(logger, size, null);
        }

        public static void ErrorWritingFrame(this ILogger logger, Exception ex)
        {
            _errorWritingFrame(logger, ex);
        }

        public static void SendFailed(this ILogger logger, Exception ex)
        {
            _sendFailed(logger, ex);
        }

        }
    }
}
