using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Microsoft.AspNetCore.Sockets;

namespace Microsoft.AspNetCore.SignalR.Internal
{
    public interface IHubProtocolResolver
    {
        IHubProtocol GetProtocol(Connection connection);
    }
}
