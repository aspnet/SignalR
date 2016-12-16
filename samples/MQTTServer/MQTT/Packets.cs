using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MQTTServer.MQTT
{
    public enum PacketType
    {
        CONNECT = 1,
        CONNACK = 2,
        DISCONNECT = 14
    }

    public class FixedHeader
    {
        public PacketType PacketType { get; set; }
        public byte Flags { get; set; }
        public int RemainingLength { get; set; }
    }
 
}
