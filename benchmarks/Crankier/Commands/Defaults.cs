using Microsoft.AspNetCore.Http.Connections;

namespace Microsoft.AspNetCore.SignalR.Crankier.Commands
{
    internal static class Defaults
    {
        public static readonly int NumberOfWorkers = 1;
        public static readonly int NumberOfConnections = 10_000;
        public static readonly int SendDurationInSeconds = 300;
        public static readonly HttpTransportType TransportType = HttpTransportType.WebSockets;
    }
}
