using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;

namespace SocketsSample.Hubs
{
    public class TestHub : Hub<dynamic>
    {
        public override Task OnConnectedAsync()
        {
            Clients.All.Notify();
            return base.OnConnectedAsync();
        }
    }
}
