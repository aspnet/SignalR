using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipelines;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Sockets;
using MQTTServer.MQTT;

namespace MQTTServer
{
    public class MQTTEndPoint : EndPoint
    {
        public override async Task OnConnectedAsync(Connection connection)
        {
            var stream = connection.Channel.GetStream();
            var fixedHeader = new FixedHeader();
            await ReadFixedHeader(stream, fixedHeader);
            await ReadRemainingDataAsync(stream, fixedHeader);


            // await ProcessRequests(connection);
        }

        private readonly byte[] _buffer = new byte[1024];

        private async Task ReadFixedHeader(Stream stream, FixedHeader fixedHeader)
        {
            await ReadThrowAsync(stream, _buffer, 0, 2);

            fixedHeader.PacketType = (PacketType)((_buffer[0] & 0xf0) >> 4);
            fixedHeader.Flags = (byte)(_buffer[0] & 0xf);
            for (var i = 1; i < 5; i++)
            {
                fixedHeader.RemainingLength <<= 7;
                fixedHeader.RemainingLength &= _buffer[i] & 0x7f;
                if ((_buffer[i] & 0x80) == 0)
                {
                    break;
                }

                await ReadThrowAsync(stream, _buffer, i, 1);
            }
        }

        private async Task ReadRemainingDataAsync(Stream stream, FixedHeader fixedHeader)
        {
            switch (fixedHeader.PacketType)
            {
                case PacketType.CONNECT:
                    await ReadConnectAsync(stream);
                    break;
                default:
                    throw new NotImplementedException();
            }
        }

        private async Task ReadConnectAsync(Stream stream)
        {
            await ReadThrowAsync(stream, _buffer, 0, 10);
            var protocolLevel = _buffer[6];
            var connectFlags = _buffer[7];
            short keepAlive = (short)((_buffer[8] << 8) | _buffer[9]);

            await WriteConnAckAsync(stream);
        }

        private async Task WriteConnAckAsync(Stream stream)
        {
            _buffer[0] = 0x20;
            _buffer[1] = 0x02;
            _buffer[2] = 0x00;  // Session present
            _buffer[3] = 0x00;  // 0x00 connection accepted

            await stream.WriteAsync(_buffer, 0, 4);
        }

        private async Task<int> ReadThrowAsync(Stream stream, byte[] buffer, int startPos, int count)
        {
            Debug.Assert(count < buffer.Length - startPos, "Buffer too small");
            var read = await stream.ReadAsync(_buffer, startPos, count);
            if (read == 0)
            {
                throw new InvalidOperationException("Connection closed");
            }

            return read;
        }
    }
}
