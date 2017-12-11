using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.AspNetCore.SignalR
{
    public interface IHubCallerClients<T> : IHubClients<T>
    {
        T Caller { get; }

        T Others { get; }
    }
}
