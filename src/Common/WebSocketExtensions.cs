using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace System.Net.WebSockets
{
    internal static class WebSocketExtensions
    {
        public static async Task SendAsync(this WebSocket webSocket, ReadOnlyBuffer<byte> buffer, WebSocketMessageType webSocketMessageType, CancellationToken cancellationToken = default)
        {
            // TODO: Consider chunking writes here if we get a multi segment buffer
#if NETCOREAPP2_1
            if (buffer.IsSingleSegment)
            {
                await webSocket.SendAsync(buffer.First, webSocketMessageType, endOfMessage: true, CancellationToken.None);
            }
            else
            {
                await webSocket.SendAsync(buffer.ToArray(), webSocketMessageType, endOfMessage: true, CancellationToken.None);
            }
#else
            if (buffer.IsSingleSegment)
            {
                var isArray = MemoryMarshal.TryGetArray(buffer.First, out var segment);
                Debug.Assert(isArray);
                await webSocket.SendAsync(segment, webSocketMessageType, endOfMessage: true, CancellationToken.None);
            }
            else
            {
                await webSocket.SendAsync(new ArraySegment<byte>(buffer.ToArray()), webSocketMessageType, true, CancellationToken.None);
            }
#endif
        }
    }
}
