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
    public class TestEndPoint : EndPoint
    {
        public ConnectionList Connections { get; } = new ConnectionList();

        public override async Task OnConnectedAsync(ConnectionContext connection)
        {
            while (await connection.Transport.In.WaitToReadAsync())
            {
                if (connection.Transport.In.TryRead(out var buffer))
                {
                    if (Encoding.UTF8.GetString(buffer) != "close")
                    {
                        await Broadcast(buffer, connection);
                    }
                    else
                    {
                        break;
                    }
                }
            }
        }

        private Task Broadcast(byte[] payload, ConnectionContext connection)
        {
            var tasks = new List<Task>(Connections.Count);
            {
                tasks.Add(connection.Transport.Out.WriteAsync(payload));
            }

            return Task.WhenAll(tasks);
        }
    }
}
