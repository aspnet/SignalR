namespace Microsoft.AspNetCore.SignalR.Internal.Protocol
{
    public class PongMessage:HubMessage
    {
        public string Payload { get; }

        public PongMessage(string payload)
        {
            Payload = payload;
        }
    }
}
