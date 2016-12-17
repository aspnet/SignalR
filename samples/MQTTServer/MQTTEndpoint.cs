using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipelines;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Sockets;
using Microsoft.Extensions.Logging;
using MQTTServer.MQTT;

namespace MQTTServer
{
    public class MQTTEndPoint : EndPoint
    {
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger _logger;

        public MQTTEndPoint(ILoggerFactory loggerFactory)
        {
            _loggerFactory = loggerFactory;
            _logger = _loggerFactory.CreateLogger<MQTTEndPoint>();
        }

        public override async Task OnConnectedAsync(Connection connection)
        {
            var buffer = new byte[1024];
            MQTTFormatter formatter = new MQTTFormatter(_loggerFactory);

            FixedHeader fixedHeader;
            var stream = connection.Channel.GetStream();
            while ((fixedHeader = await formatter.ReadFixedHeaderAsync(stream)) != null)
            {
                // TODO: loop and read all the data
                if (fixedHeader.RemainingLength > buffer.Length)
                {
                    throw new NotSupportedException($"Messages bigger than {buffer.Length} bytes currently not supported.");
                }

                await formatter.ReadRemainingDataAsync(stream, buffer, fixedHeader.RemainingLength);
                await ProcessPacket(stream, fixedHeader, buffer, formatter);
            }
        }

        private async Task ProcessPacket(Stream stream, FixedHeader header, byte[] buffer, MQTTFormatter formatter)
        {
            switch(header.PacketType)
            {
                case PacketType.CONNECT:
                    await formatter.WriteCONNACKAsync(stream);
                    break;
                case PacketType.SUBSCRIBE:
                    await formatter.WriteSUBACKAsync(stream, ReadPackageId(buffer, 0));
                    break;
                case PacketType.PUBLISH:
                    var topicLength = ReadShort(buffer, 0);
                    var topic = Encoding.UTF8.GetString(buffer, 2, topicLength);
                    var message = Encoding.UTF8.GetString(buffer, 2 + topicLength, header.RemainingLength - 2 - topicLength);
                    _logger.LogInformation("Received PUBLISH for topic '{0}', message '{1}'", topic, message);
                    await formatter.WritePUBACKAsync(stream, ReadPackageId(buffer, 2 + topicLength));
                    break;
                case PacketType.PUBCOMP:
                    // TODO: handle if/when tracking package re-delivery etc.
                    break;
                case PacketType.PINGREQ:
                    await formatter.WritePINGRESPAsync(stream);
                    break;
                default:
                    _logger.LogError("Unsupported packet type {0}", header.PacketType);
                    break;
            }
        }

        private short ReadPackageId(byte[] buffer, int offset)
        {
            return ReadShort(buffer, offset);
        }

        private short ReadShort(byte[] buffer, int offset)
        {
            return (short)((buffer[offset] << 8) | buffer[offset + 1]);
        }

    }
}
