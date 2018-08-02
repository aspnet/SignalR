// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;

namespace Microsoft.AspNetCore.SignalR.Tests
{
    public class WebSocketConnectionHandler : ConnectionHandler
    {
        public override async Task OnConnectedAsync(ConnectionContext connection)
        {
            using (var websocket = WebSocketProtocol.CreateFromStream(new PipeDuplexStream(connection.Transport), isServer: true, subProtocol: null, keepAliveInterval: TimeSpan.FromMinutes(2)))
            {
                var memory = new ArraySegment<byte>(new byte[4096]);

                while (true)
                {
                    var result = await websocket.ReceiveAsync(memory, default);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await websocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", default);
                        break;
                    }

                    await websocket.SendAsync(new ArraySegment<byte>(memory.Array, memory.Offset, result.Count), result.MessageType, result.EndOfMessage, default);
                }
            }
        }
    }
}