
namespace Microsoft.AspNetCore.Sockets
{
    public partial class HttpConnectionDispatcher
    {
        private static class Log
        {
            private static readonly Action<ILogger, string, Exception> _connectionDisposed =
                LoggerMessage.Define<string>(LogLevel.Debug, new EventId(1, nameof(ConnectionDisposed)), "Connection Id {connectionId} was disposed.");

            private static readonly Action<ILogger, string, string, Exception> _connectionAlreadyActive =
                LoggerMessage.Define<string, string>(LogLevel.Debug, new EventId(2, nameof(ConnectionAlreadyActive)), "Connection Id {connectionId} is already active via {requestId}.");

            private static readonly Action<ILogger, string, string, Exception> _pollCanceled =
                LoggerMessage.Define<string, string>(LogLevel.Debug, new EventId(3, nameof(PollCanceled)), "Previous poll canceled for {connectionId} on {requestId}.");

            private static readonly Action<ILogger, Exception> _establishedConnection =
                LoggerMessage.Define(LogLevel.Debug, new EventId(4, nameof(EstablishedConnection)), "Establishing new connection.");

            private static readonly Action<ILogger, Exception> _resumingConnection =
                LoggerMessage.Define(LogLevel.Debug, new EventId(5, nameof(ResumingConnection)), "Resuming existing connection.");

            private static readonly Action<ILogger, long, Exception> _receivedBytes =
                LoggerMessage.Define<long>(LogLevel.Debug, new EventId(6, nameof(ReceivedBytes)), "Received {count} bytes.");

            private static readonly Action<ILogger, TransportType, Exception> _transportNotSupported =
                LoggerMessage.Define<TransportType>(LogLevel.Debug, new EventId(7, nameof(TransportNotSupported)), "{transportType} transport not supported by this endpoint type.");

            private static readonly Action<ILogger, TransportType, TransportType, Exception> _cannotChangeTransport =
                LoggerMessage.Define<TransportType, TransportType>(LogLevel.Debug, new EventId(8, nameof(CannotChangeTransport)), "Cannot change transports mid-connection; currently using {transportType}, requesting {requestedTransport}.");

            private static readonly Action<ILogger, Exception> _postNotallowedForWebsockets =
                LoggerMessage.Define(LogLevel.Debug, new EventId(9, nameof(PostNotAllowedForWebSockets)), "POST requests are not allowed for websocket connections.");

            private static readonly Action<ILogger, Exception> _negotiationRequest =
                LoggerMessage.Define(LogLevel.Debug, new EventId(10, nameof(NegotiationRequest)), "Sending negotiation response.");

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

        }
    }
}