using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Microsoft.AspNetCore.Sockets;
using Newtonsoft.Json;

namespace Microsoft.AspNetCore.SignalR.Internal
{
    public class DefaultHubProtocolResolver : IHubProtocolResolver
    {
        public IHubProtocol GetProtocol(Connection connection)
        {
            // TODO: Allow customization of this serializer!
            return new JsonHubProtocol(new JsonSerializer());
        }
    }
}
