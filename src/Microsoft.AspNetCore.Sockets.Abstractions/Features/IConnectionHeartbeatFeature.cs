using System;

namespace Microsoft.AspNetCore.Sockets.Features
{
    public interface IConnectionHeartbeatFeature
    {
        void OnHeartbeat(Action<object> action, object state);
    }
}
