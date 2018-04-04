using Newtonsoft.Json.Linq;

namespace Microsoft.AspNetCore.SignalR.Crankier
{
    public class Message
    {
        public string Command { get; set; }

        public JToken Value { get; set; }
    }
}
