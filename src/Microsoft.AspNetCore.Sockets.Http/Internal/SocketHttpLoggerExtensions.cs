// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Net.WebSockets;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Sockets.Internal
{
    internal static class SocketHttpLoggerExtensions
    {
        public static void LongPolling204(this ILogger logger)
        {
            _longPolling204(logger, null);
        }

        public static void PollTimedOut(this ILogger logger)
        {
            _pollTimedOut(logger, null);
        }

        public static void LongPollingWritingMessage(this ILogger logger, long count)
        {
            _longPollingWritingMessage(logger, count, null);
        }

        public static void LongPollingDisconnected(this ILogger logger)
        {
            _longPollingDisconnected(logger, null);
        }

        public static void LongPollingTerminated(this ILogger logger, Exception ex)
        {
            _longPollingTerminated(logger, ex);
        }

        public static void ConnectionDisposed(this ILogger logger, string connectionId)
        {
            _connectionDisposed(logger, connectionId, null);
        }

        public static void ConnectionAlreadyActive(this ILogger logger, string connectionId, string requestId)
        {
            _connectionAlreadyActive(logger, connectionId, requestId, null);
        }

        public static void PollCanceled(this ILogger logger, string connectionId, string requestId)
        {
            _pollCanceled(logger, connectionId, requestId, null);
        }

        public static void EstablishedConnection(this ILogger logger)
        {
            _establishedConnection(logger, null);
        }

        public static void ResumingConnection(this ILogger logger)
        {
            _resumingConnection(logger, null);
        }

        public static void ReceivedBytes(this ILogger logger, long count)
        {
            _receivedBytes(logger, count, null);
        }

        public static void TransportNotSupported(this ILogger logger, TransportType transport)
        {
            _transportNotSupported(logger, transport, null);
        }

        public static void CannotChangeTransport(this ILogger logger, TransportType transport, TransportType requestTransport)
        {
            _cannotChangeTransport(logger, transport, requestTransport, null);
        }

        public static void PostNotAllowedForWebSockets(this ILogger logger)
        {
            _postNotallowedForWebsockets(logger, null);
        }

        public static void NegotiationRequest(this ILogger logger)
        {
            _negotiationRequest(logger, null);
        }

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

        public static void SSEWritingMessage(this ILogger logger, long count)
        {
            _sseWritingMessage(logger, count, null);
        }
    }
}
