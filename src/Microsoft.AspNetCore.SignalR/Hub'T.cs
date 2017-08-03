using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.AspNetCore.SignalR
{
    public class Hub<T> : Hub where T : class
    {
        private IHubClients<T> _clients;

        public new IHubClients<T> Clients
        {
            get
            {
                return _clients ?? new TypedHubClients<T>(base.Clients);
            }
            set { _clients = value; }
        }
       

    }
}
