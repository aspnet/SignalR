// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Sockets;

namespace Microsoft.AspNetCore.SignalR.Test.Server
{
    public class MultipleMessagesEndPoint : EndPoint
    {
        public ConnectionList Connections { get; } = new ConnectionList();

        public override async Task OnConnectedAsync(ConnectionContext connection)
        {
            try
            {
                Connections.Add(connection);
                while (await connection.Transport.In.WaitToReadAsync())
                {
                    while (connection.Transport.In.TryRead(out var buffer))
                    {
                        if (Encoding.UTF8.GetString(buffer) != "close")
                        {
                            await Broadcast(buffer);
                        }
                        else
                        {
                            return;
                        }
                    }
                }
            }
            finally
            {
                Connections.Remove(connection);
            }
        }

        private async Task Broadcast(byte[] payload)
        {
            foreach(var connection in Connections)
            {
                while (await connection.Transport.Out.WaitToWriteAsync())
                {
                    if (connection.Transport.Out.TryWrite(payload))
                    {
                        break;
                    }
                }
            }
           
        }
    }
}
