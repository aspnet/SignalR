// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.SignalR.Internal
{
    public partial class DefaultHubDispatcher<THub>
    {
        private static class Log
        {
            private static readonly Action<ILogger, InvocationMessage, Exception> _receivedHubInvocation =
                LoggerMessage.Define<InvocationMessage>(LogLevel.Debug, new EventId(1, "ReceivedHubInvocation"), "Received hub invocation: {InvocationMessage}.");

            private static readonly Action<ILogger, string, Exception> _unsupportedMessageReceived =
                LoggerMessage.Define<string>(LogLevel.Error, new EventId(2, "UnsupportedMessageReceived"), "Received unsupported message of type '{MessageType}'.");

            private static readonly Action<ILogger, string, Exception> _unknownHubMethod =
                LoggerMessage.Define<string>(LogLevel.Error, new EventId(3, "UnknownHubMethod"), "Unknown hub method '{HubMethod}'.");

            private static readonly Action<ILogger, Exception> _outboundChannelClosed =
                LoggerMessage.Define(LogLevel.Warning, new EventId(4, "OutboundChannelClosed"), "Outbound channel was closed while trying to write hub message.");

            private static readonly Action<ILogger, string, Exception> _hubMethodNotAuthorized =
                LoggerMessage.Define<string>(LogLevel.Debug, new EventId(5, "HubMethodNotAuthorized"), "Failed to invoke '{HubMethod}' because user is unauthorized.");

            private static readonly Action<ILogger, string, string, Exception> _streamingResult =
                LoggerMessage.Define<string, string>(LogLevel.Trace, new EventId(6, "StreamingResult"), "InvocationId {InvocationId}: Streaming result of type '{ResultType}'.");

            private static readonly Action<ILogger, string, string, Exception> _sendingResult =
                LoggerMessage.Define<string, string>(LogLevel.Trace, new EventId(7, "SendingResult"), "InvocationId {InvocationId}: Sending result of type '{ResultType}'.");

            private static readonly Action<ILogger, string, Exception> _failedInvokingHubMethod =
                LoggerMessage.Define<string>(LogLevel.Error, new EventId(8, "FailedInvokingHubMethod"), "Failed to invoke hub method '{HubMethod}'.");

            private static readonly Action<ILogger, string, string, Exception> _hubMethodBound =
                LoggerMessage.Define<string, string>(LogLevel.Trace, new EventId(9, "HubMethodBound"), "'{HubName}' hub method '{HubMethod}' is bound.");

            private static readonly Action<ILogger, string, Exception> _cancelStream =
                LoggerMessage.Define<string>(LogLevel.Debug, new EventId(10, "CancelStream"), "Canceling stream for invocation {InvocationId}.");

            private static readonly Action<ILogger, Exception> _unexpectedCancel =
                LoggerMessage.Define(LogLevel.Debug, new EventId(11, "UnexpectedCancel"), "CancelInvocationMessage received unexpectedly.");

            private static readonly Action<ILogger, StreamInvocationMessage, Exception> _receivedStreamHubInvocation =
                LoggerMessage.Define<StreamInvocationMessage>(LogLevel.Debug, new EventId(12, "ReceivedStreamHubInvocation"), "Received stream hub invocation: {InvocationMessage}.");

            private static readonly Action<ILogger, HubMethodInvocationMessage, Exception> _streamingMethodCalledWithInvoke =
                LoggerMessage.Define<HubMethodInvocationMessage>(LogLevel.Error, new EventId(13, "StreamingMethodCalledWithInvoke"), "A streaming method was invoked in the non-streaming fashion : {InvocationMessage}.");

            private static readonly Action<ILogger, HubMethodInvocationMessage, Exception> _nonStreamingMethodCalledWithStream =
                LoggerMessage.Define<HubMethodInvocationMessage>(LogLevel.Error, new EventId(14, "NonStreamingMethodCalledWithStream"), "A non-streaming method was invoked in the streaming fashion : {InvocationMessage}.");

            private static readonly Action<ILogger, string, Exception> _invalidReturnValueFromStreamingMethod =
                LoggerMessage.Define<string>(LogLevel.Error, new EventId(15, "InvalidReturnValueFromStreamingMethod"), "A streaming method returned a value that cannot be used to build enumerator {HubMethod}.");

            public static void ReceivedHubInvocation(ILogger logger, InvocationMessage invocationMessage)
            {
                _receivedHubInvocation(logger, invocationMessage, null);
            }

            public static void UnsupportedMessageReceived(ILogger logger, string messageType)
            {
                _unsupportedMessageReceived(logger, messageType, null);
            }

            public static void UnknownHubMethod(ILogger logger, string hubMethod)
            {
                _unknownHubMethod(logger, hubMethod, null);
            }

            public static void OutboundChannelClosed(ILogger logger)
            {
                _outboundChannelClosed(logger, null);
            }

            public static void HubMethodNotAuthorized(ILogger logger, string hubMethod)
            {
                _hubMethodNotAuthorized(logger, hubMethod, null);
            }

            public static void StreamingResult(ILogger logger, string invocationId, ObjectMethodExecutor objectMethodExecutor)
            {
                var resultType = objectMethodExecutor.AsyncResultType == null ? objectMethodExecutor.MethodReturnType : objectMethodExecutor.AsyncResultType;
                _streamingResult(logger, invocationId, resultType.FullName, null);
            }

            public static void SendingResult(ILogger logger, string invocationId, ObjectMethodExecutor objectMethodExecutor)
            {
                var resultType = objectMethodExecutor.AsyncResultType == null ? objectMethodExecutor.MethodReturnType : objectMethodExecutor.AsyncResultType;
                _sendingResult(logger, invocationId, resultType.FullName, null);
            }

            public static void FailedInvokingHubMethod(ILogger logger, string hubMethod, Exception exception)
            {
                _failedInvokingHubMethod(logger, hubMethod, exception);
            }

            public static void HubMethodBound(ILogger logger, string hubName, string hubMethod)
            {
                _hubMethodBound(logger, hubName, hubMethod, null);
            }

            public static void CancelStream(ILogger logger, string invocationId)
            {
                _cancelStream(logger, invocationId, null);
            }

            public static void UnexpectedCancel(ILogger logger)
            {
                _unexpectedCancel(logger, null);
            }

            public static void ReceivedStreamHubInvocation(ILogger logger, StreamInvocationMessage invocationMessage)
            {
                _receivedStreamHubInvocation(logger, invocationMessage, null);
            }

            public static void StreamingMethodCalledWithInvoke(ILogger logger, HubMethodInvocationMessage invocationMessage)
            {
                _streamingMethodCalledWithInvoke(logger, invocationMessage, null);
            }

            public static void NonStreamingMethodCalledWithStream(ILogger logger, HubMethodInvocationMessage invocationMessage)
            {
                _nonStreamingMethodCalledWithStream(logger, invocationMessage, null);
            }

            public static void InvalidReturnValueFromStreamingMethod(ILogger logger, string hubMethod)
            {
                _invalidReturnValueFromStreamingMethod(logger, hubMethod, null);
            }
        }
    }
}
