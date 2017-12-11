using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.AspNetCore.SignalR
{
    public interface IHubCallerClients : IHubCallerClients<IClientProxy> { }
}
