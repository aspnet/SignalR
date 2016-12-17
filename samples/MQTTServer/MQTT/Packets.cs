namespace MQTTServer.MQTT
{
    public enum PacketType : byte
    {
        CONNECT = 1,
        CONNACK = 2,
        PUBLISH = 3,
        PUBACK = 4,
        PUBREC = 5,
        PUBREL = 6,
        PUBCOMP = 7,
        SUBSCRIBE = 8,
        SUBACK = 9,
        UNSUBSCRIBE = 10,
        UNSUBACK = 11,
        PINGREQ = 12,
        PINGRESP = 13,
        DISCONNECT = 14
    }

    public class FixedHeader
    {
        public PacketType PacketType { get; set; }
        public byte Flags { get; set; }
        public int RemainingLength { get; set; }
    }
}
