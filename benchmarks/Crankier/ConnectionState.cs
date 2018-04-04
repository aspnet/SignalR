namespace Microsoft.AspNetCore.SignalR.Crankier
{
    public enum ConnectionState
    {
        Connecting,
        Connected,
        Reconnecting,
        Disconnected,
        Faulted,
    }
}
