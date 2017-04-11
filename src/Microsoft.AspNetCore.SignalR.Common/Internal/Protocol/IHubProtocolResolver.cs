using Microsoft.AspNetCore.Http;

namespace Microsoft.AspNetCore.SignalR.Internal.Protocol
{
    public interface IHubProtocolResolver
    {
        IHubProtocol GetProtocol(HttpContext context);
    }
}
