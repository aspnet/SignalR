namespace Microsoft.AspNetCore.SignalR.Internal.Protocol
{
    public class PingMessage : HubMessage
    {
        public string Payload { get; }

        public PingMessage(string payload)
        {
            Payload = payload;
        }
    }
}
