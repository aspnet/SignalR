// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Net.WebSockets;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Sockets.Internal
{
    public static class SocketLoggerExtensions
    {
        // Category: LongPollingTransport & ServerSentEventsTransport
        private static readonly Action<ILogger, DateTime, string, Exception> _longPolling204 =
            LoggerMessage.Define<DateTime, string>(LogLevel.Information, 0, "{time}: Terminating Long Polling connection by sending 204 response to request {requestId}.");

        private static readonly Action<ILogger, DateTime, string, Exception> _pollTimedOut =
            LoggerMessage.Define<DateTime, string>(LogLevel.Information, 1, "{time}: Poll request timed out. Sending 200 response to request {requestId}.");

        private static readonly Action<ILogger, DateTime, int, string, Exception> _writingMessage =
            LoggerMessage.Define<DateTime, int, string>(LogLevel.Debug, 2, "{time}: Writing a {count} byte message to request {requestId}.");

        private static readonly Action<ILogger, DateTime, string, Exception> _longPollingDisconnected =
            LoggerMessage.Define<DateTime, string>(LogLevel.Debug, 3, "{time}: Client disconnected from Long Polling endpoint for request {requestId}.");

        private static readonly Action<ILogger, DateTime, string, Exception> _longPollingTerminated =
            LoggerMessage.Define<DateTime, string>(LogLevel.Error, 4, "{time}: Long Polling transport was terminated due to an error on request {requestId}.");

        // Category: HttpConnectionDispatcher
        private static readonly Action<ILogger, DateTime, string, Exception> _connectionDisposed =
            LoggerMessage.Define<DateTime, string>(LogLevel.Debug, 0, "{time}: Connection {connectionId} was disposed.");

        private static readonly Action<ILogger, DateTime, string, string, Exception> _connectionAlreadyActive =
            LoggerMessage.Define<DateTime, string, string>(LogLevel.Debug, 1, "{time}: Connection {connectionId} is already active via {requestId}.");

        private static readonly Action<ILogger, DateTime, string, string, Exception> _pollCanceled =
            LoggerMessage.Define<DateTime, string, string>(LogLevel.Debug, 2, "{time}: Previous poll canceled for {connectionId} on {requestId}.");

        private static readonly Action<ILogger, DateTime, string, string, Exception> _establishedConnection =
            LoggerMessage.Define<DateTime, string, string>(LogLevel.Debug, 3, "{time}: Establishing new connection: {connectionId} on {requestId}.");

        private static readonly Action<ILogger, DateTime, string, string, Exception> _resumingConnection =
            LoggerMessage.Define<DateTime, string, string>(LogLevel.Debug, 4, "{time}: Resuming existing connection: {connectionId} on {requestId}.");

        private static readonly Action<ILogger, DateTime, int, string, Exception> _receivedBytes =
            LoggerMessage.Define<DateTime, int, string>(LogLevel.Debug, 5, "{time}: Received {count} bytes from connection {connectionId}.");

        private static readonly Action<ILogger, DateTime, TransportType, string, Exception> _transportNotSupported =
            LoggerMessage.Define<DateTime, TransportType, string>(LogLevel.Debug, 6, "{time}: {transportType} transport not supported by this endpoint type from connection {connectionId}.");

        private static readonly Action<ILogger, DateTime, TransportType, TransportType, string, Exception> _cannotChangeTransport =
            LoggerMessage.Define<DateTime, TransportType, TransportType, string>(LogLevel.Debug, 7, "{time}: Cannot change transports mid-connection; currently using {transportType}, requesting {requestedTransport} from connection {connectionId}.");

        private static readonly Action<ILogger, DateTime, string, Exception> _negotiationRequest =
            LoggerMessage.Define<DateTime, string>(LogLevel.Debug, 8, "{time}: Sending negotiation response to connection {connectionId}.");

        // Category: WebSocketsTransport
        private static readonly Action<ILogger, DateTime, string, Exception> _socketOpened =
            LoggerMessage.Define<DateTime, string>(LogLevel.Information, 0, "{time}: Socket opened for request {requestId}.");

        private static readonly Action<ILogger, DateTime, string, Exception> _socketClosed =
            LoggerMessage.Define<DateTime, string>(LogLevel.Information, 1, "{time}: Socket closed for request {requestId}.");

        private static readonly Action<ILogger, DateTime, WebSocketCloseStatus?, string, string, Exception> _clientClosed =
            LoggerMessage.Define<DateTime, WebSocketCloseStatus?, string, string>(LogLevel.Debug, 2, "{time}: Client closed connection with status code '{status}' ({description}). Signaling end-of-input to application for request {requestId}.");

        private static readonly Action<ILogger, DateTime, string, Exception> _waitingForSend =
            LoggerMessage.Define<DateTime, string>(LogLevel.Debug, 3, "{time}: Waiting for the application to finish sending data for request {requestId}.");

        private static readonly Action<ILogger, DateTime, string, Exception> _failedSending =
            LoggerMessage.Define<DateTime, string>(LogLevel.Debug, 4, "{time}: Application failed during sending. Sending InternalServerError close frame to request {requestId}.");

        private static readonly Action<ILogger, DateTime, string, Exception> _finishedSending =
            LoggerMessage.Define<DateTime, string>(LogLevel.Debug, 5, "{time}: Application finished sending. Sending close frame to request {requestId}.");

        private static readonly Action<ILogger, DateTime, string, Exception> _waitingForClose =
            LoggerMessage.Define<DateTime, string>(LogLevel.Debug, 6, "{time}: Waiting for the client to close the socket for request {requestId}.");

        private static readonly Action<ILogger, DateTime, string, Exception> _closeTimedOut =
            LoggerMessage.Define<DateTime, string>(LogLevel.Debug, 7, "{time}: Timed out waiting for client to send the close frame, aborting the connection for request {requestId}.");

        private static readonly Action<ILogger, DateTime, string, WebSocketMessageType, int, bool, Exception> _messageReceived =
            LoggerMessage.Define<DateTime, string, WebSocketMessageType, int, bool>(LogLevel.Debug, 8, "{time}: Message received from request {requestId}. Type: {messageType}, size: {size}, EndOfMessage: {endOfMessage}.");

        private static readonly Action<ILogger, DateTime, string, int, Exception> _messageToApplication =
            LoggerMessage.Define<DateTime, string, int>(LogLevel.Debug, 9, "{time}: Passing message to application from request {requestId}. Payload size: {size}.");

        private static readonly Action<ILogger, DateTime, string, int, Exception> _sendPayload =
            LoggerMessage.Define<DateTime, string, int>(LogLevel.Debug, 10, "{time}: Sending payload to request {requestId}: {size} bytes.");

        private static readonly Action<ILogger, DateTime, string, Exception> _errorWritingFrame =
            LoggerMessage.Define<DateTime, string>(LogLevel.Error, 11, "{time}: Error writing frame to request {requestId}.");

        public static void LongPolling204(this ILogger logger, string requestId)
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                _longPolling204(logger, DateTime.Now, requestId, null);
            }
        }

        public static void PollTimedOut(this ILogger logger, string requestId)
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                _pollTimedOut(logger, DateTime.Now, requestId, null);
            }
        }

        public static void WritingMessage(this ILogger logger, int count, string requestId)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                _writingMessage(logger, DateTime.Now, count, requestId, null);
            }
        }

        public static void LongPollingDisconnected(this ILogger logger, string requestId)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                _longPollingDisconnected(logger, DateTime.Now, requestId, null);
            }
        }

        public static void LongPollingTerminated(this ILogger logger, string requestId, Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                _longPollingTerminated(logger, DateTime.Now, requestId, ex);
            }
        }

        public static void ConnectionDisposed(this ILogger logger, string connectionId)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                _connectionDisposed(logger, DateTime.Now, connectionId, null);
            }
        }

        public static void ConnectionAlreadyActive(this ILogger logger, string connectionId, string requestId)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                _connectionAlreadyActive(logger, DateTime.Now, connectionId, requestId, null);
            }
        }

        public static void PollCanceled(this ILogger logger, string connectionId, string requestId)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                _pollCanceled(logger, DateTime.Now, connectionId, requestId, null);
            }
        }

        public static void EstablishedConnection(this ILogger logger, string connectionId, string requestId)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                _establishedConnection(logger, DateTime.Now, connectionId, requestId, null);
            }
        }

        public static void ResumingConnection(this ILogger logger, string connectionId, string requestId)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                _resumingConnection(logger, DateTime.Now, connectionId, requestId, null);
            }
        }

        public static void ReceivedBytes(this ILogger logger, int count, string connectionId)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                _receivedBytes(logger, DateTime.Now, count, connectionId, null);
            }
        }

        public static void TransportNotSupported(this ILogger logger, TransportType transport, string connectionId)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                _transportNotSupported(logger, DateTime.Now, transport, connectionId, null);
            }
        }

        public static void CannotChangeTransport(this ILogger logger, TransportType transport, TransportType requestTransport, string connectionId)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                _cannotChangeTransport(logger, DateTime.Now, transport, requestTransport, connectionId, null);
            }
        }

        public static void NegotiationRequest(this ILogger logger, string connectionId)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                _negotiationRequest(logger, DateTime.Now, connectionId, null);
            }
        }

        public static void SocketOpened(this ILogger logger, string requestId)
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                _socketOpened(logger, DateTime.Now, requestId, null);
            }
        }

        public static void SocketClosed(this ILogger logger, string requestId)
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                _socketClosed(logger, DateTime.Now, requestId, null);
            }
        }

        public static void ClientClosed(this ILogger logger, WebSocketCloseStatus? closeStatus, string closeDescription, string requestId)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                _clientClosed(logger, DateTime.Now, closeStatus, closeDescription, requestId, null);
            }
        }

        public static void WaitingForSend(this ILogger logger, string requestId)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                _waitingForSend(logger, DateTime.Now, requestId, null);
            }
        }

        public static void FailedSending(this ILogger logger, string requestId)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                _failedSending(logger, DateTime.Now, requestId, null);
            }
        }

        public static void FinishedSending(this ILogger logger, string requestId)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                _finishedSending(logger, DateTime.Now, requestId, null);
            }
        }

        public static void WaitingForClose(this ILogger logger, string requestId)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                _waitingForClose(logger, DateTime.Now, requestId, null);
            }
        }

        public static void CloseTimedOut(this ILogger logger, string requestId)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                _closeTimedOut(logger, DateTime.Now, requestId, null);
            }
        }

        public static void MessageReceived(this ILogger logger, string requestId, WebSocketMessageType type, int size, bool endOfMessage)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                _messageReceived(logger, DateTime.Now, requestId, type, size, endOfMessage, null);
            }
        }

        public static void MessageToApplication(this ILogger logger, string requestId, int size)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                _messageToApplication(logger, DateTime.Now, requestId, size, null);
            }
        }

        public static void SendPayload(this ILogger logger, string requestId, int size)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                _sendPayload(logger, DateTime.Now, requestId, size, null);
            }
        }

        public static void ErrorWritingFrame(this ILogger logger, string requestId, Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                _errorWritingFrame(logger, DateTime.Now, requestId, ex);
            }
        }
    }
}
