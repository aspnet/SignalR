using System;

namespace Microsoft.AspNetCore.Sockets
{
    [Flags]
    public enum TransportType
    {
        /// <summary>
        /// Every transport
        /// </summary>
        All = Streaming | LongPolling,

        /// <summary>
        /// All transports except for long-polling
        /// </summary>
        Streaming = WebSockets | ServerSentEvents,

        WebSockets = 1,
        ServerSentEvents = 2,
        LongPolling = 4
    }
}
