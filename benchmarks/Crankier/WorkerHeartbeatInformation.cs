
namespace Microsoft.AspNetCore.SignalR.Crankier
{
    public class WorkerHeartbeatInformation
    {
        public int Id { get; set; }

        public int ConnectedCount { get; set; }

        public int DisconnectedCount { get; set; }

        public int ReconnectingCount { get; set; }

        public int TargetConnectionCount { get; set; }
    }
}
