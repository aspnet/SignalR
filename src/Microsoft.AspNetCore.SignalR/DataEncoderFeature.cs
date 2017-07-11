using System;
using System.Text;

namespace Microsoft.AspNetCore.SignalR
{
    public interface IDataEncoderFeature
    {
        IDataEncoder Encoder { get; set; }
    }

    public class DataEncoderFeature : IDataEncoderFeature
    {
        public IDataEncoder Encoder { get; set; }
    }

    public interface IDataEncoder
    {
        byte[] Encode(byte[] message);
    }

    public class PassThroughEncoder : IDataEncoder
    {
        public byte[] Encode(byte[] message)
        {
            return message;
        }
    }

    public class Base64Encoder : IDataEncoder
    {
        public byte[] Encode(byte[] message)
        {
            return Encoding.UTF8.GetBytes(Convert.ToBase64String(message));
        }
    }
}
