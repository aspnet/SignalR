﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Sockets.Client
{
    public partial class ServerSentEventsTransport
    {
        private static class Log
        {
            private static readonly Action<ILogger, TransferMode, Exception> _startTransport =
                LoggerMessage.Define<TransferMode>(LogLevel.Information, new EventId(1, "StartTransport"), "Starting transport. Transfer mode: {transferMode}.");

            private static readonly Action<ILogger, Exception> _transportStopped =
                LoggerMessage.Define(LogLevel.Debug, new EventId(2, "TransportStopped"), "Transport stopped.");

            private static readonly Action<ILogger, Exception> _startReceive =
                LoggerMessage.Define(LogLevel.Debug, new EventId(3, "StartReceive"), "Starting receive loop.");

            private static readonly Action<ILogger, Exception> _receiveStopped =
                LoggerMessage.Define(LogLevel.Debug, new EventId(4, "ReceiveStopped"), "Receive loop stopped.");

            private static readonly Action<ILogger, Exception> _receiveCanceled =
                LoggerMessage.Define(LogLevel.Debug, new EventId(5, "ReceiveCanceled"), "Receive loop canceled.");

            private static readonly Action<ILogger, Exception> _transportStopping =
                LoggerMessage.Define(LogLevel.Information, new EventId(6, "TransportStopping"), "Transport is stopping.");

            // EventId's 7 - 13 used in SendUtils

            private static readonly Action<ILogger, int, Exception> _messageToApp =
                LoggerMessage.Define<int>(LogLevel.Debug, new EventId(14, "MessageToApp"), "Passing message to application. Payload size: {count}.");

            private static readonly Action<ILogger, Exception> _eventStreamEnded =
                LoggerMessage.Define(LogLevel.Debug, new EventId(15, "EventStreamEnded"), "Server-Sent Event Stream ended.");

            private static readonly Action<ILogger, long, Exception> _parsingSSE =
                LoggerMessage.Define<long>(LogLevel.Debug, new EventId(16, "ParsingSSE"), "Received {count} bytes. Parsing SSE frame.");

            public static void StartTransport(ILogger logger, TransferMode transferMode)
            {
                _startTransport(logger, transferMode, null);
            }

            public static void TransportStopped(ILogger logger, Exception exception)
            {
                _transportStopped(logger, exception);
            }

            public static void StartReceive(ILogger logger)
            {
                _startReceive(logger, null);
            }

            public static void TransportStopping(ILogger logger)
            {
                _transportStopping(logger, null);
            }

            public static void MessageToApp(ILogger logger, int count)
            {
                _messageToApp(logger, count, null);
            }

            public static void ReceiveCanceled(ILogger logger)
            {
                _receiveCanceled(logger, null);
            }

            public static void ReceiveStopped(ILogger logger)
            {
                _receiveStopped(logger, null);
            }

            public static void EventStreamEnded(ILogger logger)
            {
                _eventStreamEnded(logger, null);
            }

            public static void ParsingSSE(ILogger logger, long bytes)
            {
                _parsingSSE(logger, bytes, null);
            }
        }
    }
}
