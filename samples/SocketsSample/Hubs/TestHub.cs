// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;

namespace SocketsSample.Hubs
{
    public class TestHub : Hub<IChatClient>
    {
        public override Task OnConnectedAsync()
        {
            Clients.All.JoinChat();
            return Task.CompletedTask;
        }
    }

    public interface IChatClient
    {
        void JoinChat();
        void LeaveChat();
        void SendHello();
    }
}
