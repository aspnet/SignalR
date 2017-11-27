using System;

namespace Microsoft.AspNetCore.Sockets.Features
{
    public class ConnectionInherentKeepAliveFeature : IConnectionInherentKeepAliveFeature
    {
        public TimeSpan KeepAliveInterval { get; }

        public ConnectionInherentKeepAliveFeature(TimeSpan keepAliveInterval)
        {
            KeepAliveInterval = keepAliveInterval;
        }
    }
}
