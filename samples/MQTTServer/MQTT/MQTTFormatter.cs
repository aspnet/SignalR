
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace MQTTServer.MQTT
{
    public class MQTTFormatter
    {
        private readonly ILogger _logger;
        private readonly byte[] _buffer = new byte[1024];

        public MQTTFormatter(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<MQTTFormatter>();
        }

        public async Task<FixedHeader> ReadFixedHeaderAsync(Stream stream)
        {
            if (await stream.ReadAsync(_buffer, 0, 1) == 0)
            {
                return null;
            }

            var fixedHeader = new FixedHeader
            {
                PacketType = (PacketType)((_buffer[0] & 0xf0) >> 4),
                Flags = (byte)(_buffer[0] & 0xf)
            };

            for (var i = 1; i < 5; i++)
            {
                if (await stream.ReadAsync(_buffer, i, 1) == 0)
                {
                    return null;
                }

                fixedHeader.RemainingLength <<= 7;
                fixedHeader.RemainingLength |= _buffer[i] & 0x7f;
                if ((_buffer[i] & 0x80) == 0)
                {
                    break;
                }
            }

            _logger.LogDebug("Received fixed header. Packet type: {0}, remaining length: {1}",
                fixedHeader.PacketType.ToString(), fixedHeader.RemainingLength);

            return fixedHeader;
        }

        public async Task<bool> ReadRemainingDataAsync(Stream stream, byte[] buffer, int bytesToRead)
        {
            var offset = 0;
            while (offset < bytesToRead)
            {
                var bytesRead = await stream.ReadAsync(buffer, offset, bytesToRead - offset);
                if (bytesRead == 0)
                {
                    return false;
                }

                offset += bytesRead;
            }

            return true;
        }

        public async Task WriteCONNACKAsync(Stream stream /*TODO: should take flags, error code*/)
        {
            _buffer[0] = (byte)PacketType.CONNACK << 4;
            _buffer[1] = 2; // remaining data
            _buffer[2] = 0x00;  // Session present
            _buffer[3] = 0x00;  // 0x00 connection accepted

            await stream.WriteAsync(_buffer, 0, 4);
        }

        public async Task WriteSUBACKAsync(Stream stream, short packageId)
        {
            _buffer[0] = (byte)PacketType.SUBACK << 4;
            _buffer[1] = 3;
            _buffer[2] = (byte)((packageId & 0xff00) >> 8);
            _buffer[3] = (byte)(packageId & 0xff);
            _buffer[4] = 0;

            await stream.WriteAsync(_buffer, 0, 4);
        }

        public async Task WritePUBACKAsync(Stream stream, short packageId)
        {
            _buffer[0] = (byte)PacketType.PUBACK << 4;
            _buffer[1] = 2;
            _buffer[2] = (byte)((packageId & 0xff00) >> 8);
            _buffer[3] = (byte)(packageId & 0xff);

            await stream.WriteAsync(_buffer, 0, 4);
        }

        public async Task WritePINGRESPAsync(Stream stream)
        {
            _buffer[0] = (byte)PacketType.PINGRESP << 4;
            _buffer[1] = 0;
            await stream.WriteAsync(_buffer, 0, 2);
        }
    }
}
