using System;
using System.Buffers;
using System.Net.WebSockets;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;

namespace SignalRSamples
{
    public class WebSocketConnectionHandler : ConnectionHandler
    {
        private static ArraySegment<byte> _emptyArraySegment = new ArraySegment<byte>(Array.Empty<byte>());
        public override async Task OnConnectedAsync(ConnectionContext connection)
        {
            using (var websocket = WebSocketProtocol.CreateFromStream(new PipeDuplexStream(connection.Transport), isServer: true, subProtocol: null, keepAliveInterval: TimeSpan.FromMinutes(2)))
            {
                while (true)
                {
                    // Bufferless async wait (well it's like 14 bytes)
                    var result = await websocket.ReceiveAsync(_emptyArraySegment, default);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }

                    // Rent 4K
                    byte[] buffer = ArrayPool<byte>.Shared.Rent(4096);
                    try
                    {
                        var memory = new ArraySegment<byte>(buffer);
                        // This should always be synchronous
                        result = await websocket.ReceiveAsync(memory, default);

                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            await websocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", default);
                            break;
                        }

                        await websocket.SendAsync(new ArraySegment<byte>(memory.Array, memory.Offset, result.Count), result.MessageType, result.EndOfMessage, default);
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(buffer);
                    }
                }
            }
        }
    }
}