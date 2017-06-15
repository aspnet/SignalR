﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Net.WebSockets;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Sockets.Internal
{
    public static class SocketLoggerExtensions
    {
        //Connection id "0HL5ILL9H6928", Request id "0HL5ILL9H6928:00000001":
        // Category: LongPollingTransport
        private static readonly Action<ILogger, DateTime, string, string, Exception> _longPolling204 =
            LoggerMessage.Define<DateTime, string, string>(LogLevel.Information, 0, "{time}: Connection Id {connectionId}, Request Id {requestId}: Terminating Long Polling connection by sending 204 response.");

        private static readonly Action<ILogger, DateTime, string, string, Exception> _pollTimedOut =
            LoggerMessage.Define<DateTime, string, string>(LogLevel.Information, 1, "{time}: Connection Id {connectionId}, Request Id {requestId}: Poll request timed out. Sending 200 response to connection.");

        private static readonly Action<ILogger, DateTime, int, string, string, Exception> _longPollingWritingMessage =
            LoggerMessage.Define<DateTime, int, string, string>(LogLevel.Debug, 2, "{time}: Connection Id {connectionId}, Request Id {requestId}: Writing a {count} byte message to connection.");

        private static readonly Action<ILogger, DateTime, string, string, Exception> _longPollingDisconnected =
            LoggerMessage.Define<DateTime, string, string>(LogLevel.Debug, 3, "{time}: Connection Id {connectionId}, Request Id {requestId}: Client disconnected from Long Polling endpoint for connection.");

        private static readonly Action<ILogger, DateTime, string, string, Exception> _longPollingTerminated =
            LoggerMessage.Define<DateTime, string, string>(LogLevel.Error, 4, "{time}: Connection Id {connectionId}, Request Id {requestId}: Long Polling transport was terminated due to an error on connection.");

        // Category: HttpConnectionDispatcher
        private static readonly Action<ILogger, DateTime, string, Exception> _connectionDisposed =
            LoggerMessage.Define<DateTime, string>(LogLevel.Debug, 0, "{time}: Connection Id {connectionId} was disposed.");

        private static readonly Action<ILogger, DateTime, string, string, Exception> _connectionAlreadyActive =
            LoggerMessage.Define<DateTime, string, string>(LogLevel.Debug, 1, "{time}: Connection Id {connectionId} is already active via {requestId}.");

        private static readonly Action<ILogger, DateTime, string, string, Exception> _pollCanceled =
            LoggerMessage.Define<DateTime, string, string>(LogLevel.Debug, 2, "{time}: Previous poll canceled for {connectionId} on {requestId}.");

        private static readonly Action<ILogger, DateTime, string, string, Exception> _establishedConnection =
            LoggerMessage.Define<DateTime, string, string>(LogLevel.Debug, 3, "{time}: Connection Id {connectionId}, Request Id {requestId}: Establishing new connection.");

        private static readonly Action<ILogger, DateTime, string, string, Exception> _resumingConnection =
            LoggerMessage.Define<DateTime, string, string>(LogLevel.Debug, 4, "{time}: Connection Id {connectionId}, Request Id {requestId}: Resuming existing connection.");

        private static readonly Action<ILogger, DateTime, string, int, Exception> _receivedBytes =
            LoggerMessage.Define<DateTime, string, int>(LogLevel.Debug, 5, "{time}: Connection Id {connectionId}: Received {count} bytes.");

        private static readonly Action<ILogger, DateTime, string, TransportType, Exception> _transportNotSupported =
            LoggerMessage.Define<DateTime, string, TransportType>(LogLevel.Debug, 6, "{time}: Connection Id {connectionId}: {transportType} transport not supported by this endpoint type.");

        private static readonly Action<ILogger, DateTime, string, TransportType, TransportType, Exception> _cannotChangeTransport =
            LoggerMessage.Define<DateTime, string, TransportType, TransportType>(LogLevel.Debug, 7, "{time}: Connection Id {connectionId}: Cannot change transports mid-connection; currently using {transportType}, requesting {requestedTransport}.");

        private static readonly Action<ILogger, DateTime, string, Exception> _negotiationRequest =
            LoggerMessage.Define<DateTime, string>(LogLevel.Debug, 8, "{time}: Connection Id {connectionId}: Sending negotiation response.");

        // Category: WebSocketsTransport
        private static readonly Action<ILogger, DateTime, string, Exception> _socketOpened =
            LoggerMessage.Define<DateTime, string>(LogLevel.Information, 0, "{time}: Connection Id {connectionId}: Socket opened.");

        private static readonly Action<ILogger, DateTime, string, Exception> _socketClosed =
            LoggerMessage.Define<DateTime, string>(LogLevel.Information, 1, "{time}: Connection Id {connectionId}: Socket closed.");

        private static readonly Action<ILogger, DateTime, string, WebSocketCloseStatus?, string, Exception> _clientClosed =
            LoggerMessage.Define<DateTime, string, WebSocketCloseStatus?, string>(LogLevel.Debug, 2, "{time}: Connection Id {connectionId}: Client closed connection with status code '{status}' ({description}). Signaling end-of-input to application..");

        private static readonly Action<ILogger, DateTime, string, Exception> _waitingForSend =
            LoggerMessage.Define<DateTime, string>(LogLevel.Debug, 3, "{time}: Connection Id {connectionId}: Waiting for the application to finish sending data.");

        private static readonly Action<ILogger, DateTime, string, Exception> _failedSending =
            LoggerMessage.Define<DateTime, string>(LogLevel.Debug, 4, "{time}: Connection Id {connectionId}: Application failed during sending. Sending InternalServerError close frame.");

        private static readonly Action<ILogger, DateTime, string, Exception> _finishedSending =
            LoggerMessage.Define<DateTime, string>(LogLevel.Debug, 5, "{time}: Connection Id {connectionId}: Application finished sending. Sending close frame.");

        private static readonly Action<ILogger, DateTime, string, Exception> _waitingForClose =
            LoggerMessage.Define<DateTime, string>(LogLevel.Debug, 6, "{time}: Connection Id {connectionId}: Waiting for the client to close the socket.");

        private static readonly Action<ILogger, DateTime, string, Exception> _closeTimedOut =
            LoggerMessage.Define<DateTime, string>(LogLevel.Debug, 7, "{time}: Connection Id {connectionId}: Timed out waiting for client to send the close frame, aborting the connection.");

        private static readonly Action<ILogger, DateTime, string, WebSocketMessageType, int, bool, Exception> _messageReceived =
            LoggerMessage.Define<DateTime, string, WebSocketMessageType, int, bool>(LogLevel.Debug, 8, "{time}: Connection Id {connectionId}: Message received. Type: {messageType}, size: {size}, EndOfMessage: {endOfMessage}.");

        private static readonly Action<ILogger, DateTime, string, int, Exception> _messageToApplication =
            LoggerMessage.Define<DateTime, string, int>(LogLevel.Debug, 9, "{time}: Connection Id {connectionId}: Passing message to application. Payload size: {size}.");

        private static readonly Action<ILogger, DateTime, string, int, Exception> _sendPayload =
            LoggerMessage.Define<DateTime, string, int>(LogLevel.Debug, 10, "{time}: Connection Id {connectionId}: Sending payload: {size} bytes.");

        private static readonly Action<ILogger, DateTime, string, Exception> _errorWritingFrame =
            LoggerMessage.Define<DateTime, string>(LogLevel.Error, 11, "{time}: Connection Id {connectionId}: Error writing frame.");

        // Category: ServerSentEventsTransport
        private static readonly Action<ILogger, DateTime, string, int, Exception> _sseWritingMessage =
            LoggerMessage.Define<DateTime, string, int>(LogLevel.Debug, 0, "{time}: Connection Id {connectionId}: Writing a {count} byte message.");

        public static void LongPolling204(this ILogger logger, string connectionId, string requestId)
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                _longPolling204(logger, DateTime.Now, connectionId, requestId, null);
            }
        }

        public static void PollTimedOut(this ILogger logger, string connectionId, string requestId)
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                _pollTimedOut(logger, DateTime.Now, connectionId, requestId, null);
            }
        }

        public static void LongPollingWritingMessage(this ILogger logger, int count, string connectionId, string requestId)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                _longPollingWritingMessage(logger, DateTime.Now, count, connectionId, requestId, null);
            }
        }

        public static void LongPollingDisconnected(this ILogger logger, string connectionId, string requestId)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                _longPollingDisconnected(logger, DateTime.Now, connectionId, requestId, null);
            }
        }

        public static void LongPollingTerminated(this ILogger logger, string connectionId, string requestId, Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                _longPollingTerminated(logger, DateTime.Now, connectionId, requestId, ex);
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

        public static void ReceivedBytes(this ILogger logger, string connectionId, int count)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                _receivedBytes(logger, DateTime.Now, connectionId, count, null);
            }
        }

        public static void TransportNotSupported(this ILogger logger, string connectionId, TransportType transport)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                _transportNotSupported(logger, DateTime.Now, connectionId, transport, null);
            }
        }

        public static void CannotChangeTransport(this ILogger logger, string connectionId, TransportType transport, TransportType requestTransport)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                _cannotChangeTransport(logger, DateTime.Now, connectionId, transport, requestTransport, null);
            }
        }

        public static void NegotiationRequest(this ILogger logger, string connectionId)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                _negotiationRequest(logger, DateTime.Now, connectionId, null);
            }
        }

        public static void SocketOpened(this ILogger logger, string connectionId)
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                _socketOpened(logger, DateTime.Now, connectionId, null);
            }
        }

        public static void SocketClosed(this ILogger logger, string connectionId)
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                _socketClosed(logger, DateTime.Now, connectionId, null);
            }
        }

        public static void ClientClosed(this ILogger logger, string connectionId, WebSocketCloseStatus? closeStatus, string closeDescription)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                _clientClosed(logger, DateTime.Now, connectionId, closeStatus, closeDescription, null);
            }
        }

        public static void WaitingForSend(this ILogger logger, string connectionId)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                _waitingForSend(logger, DateTime.Now, connectionId, null);
            }
        }

        public static void FailedSending(this ILogger logger, string connectionId)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                _failedSending(logger, DateTime.Now, connectionId, null);
            }
        }

        public static void FinishedSending(this ILogger logger, string connectionId)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                _finishedSending(logger, DateTime.Now, connectionId, null);
            }
        }

        public static void WaitingForClose(this ILogger logger, string connectionId)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                _waitingForClose(logger, DateTime.Now, connectionId, null);
            }
        }

        public static void CloseTimedOut(this ILogger logger, string connectionId)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                _closeTimedOut(logger, DateTime.Now, connectionId, null);
            }
        }

        public static void MessageReceived(this ILogger logger, string connectionId, WebSocketMessageType type, int size, bool endOfMessage)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                _messageReceived(logger, DateTime.Now, connectionId, type, size, endOfMessage, null);
            }
        }

        public static void MessageToApplication(this ILogger logger, string connectionId, int size)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                _messageToApplication(logger, DateTime.Now, connectionId, size, null);
            }
        }

        public static void SendPayload(this ILogger logger, string connectionId, int size)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                _sendPayload(logger, DateTime.Now, connectionId, size, null);
            }
        }

        public static void ErrorWritingFrame(this ILogger logger, string connectionId, Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                _errorWritingFrame(logger, DateTime.Now, connectionId, ex);
            }
        }

        public static void SSEWritingMessage(this ILogger logger, string connectionId, int count)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                _sseWritingMessage(logger, DateTime.Now, connectionId, count, null);
            }
        }
    }
}
