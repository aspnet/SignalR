using System;
using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.SignalR.Internal;
using Microsoft.AspNetCore.SignalR.Internal.Encoders;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;

namespace Microsoft.AspNetCore.SignalR.Microbenchmarks
{
    [ParameterizedJobConfig(typeof(CoreConfig))]
    public class HubProtocolBenchmark
    {
        private HubProtocolReaderWriter _hubProtocolReaderWriter;
        private byte[] _binaryInput;
        private TestBinder _binder;
        private HubMessage _hubMessage;

        [Params(Message.Small, Message.Medium, Message.Large)]
        public Message Input { get; set; }

        [Params(Protocol.MsgPack, Protocol.Json)]
        public Protocol HubProtocol { get; set; }

        [GlobalSetup]
        public void GlobalSetup()
        {
            switch (HubProtocol)
            {
                case Protocol.MsgPack:
                    _hubProtocolReaderWriter = new HubProtocolReaderWriter(new MessagePackHubProtocol(), new PassThroughEncoder());
                    break;
                case Protocol.Json:
                    _hubProtocolReaderWriter = new HubProtocolReaderWriter(new JsonHubProtocol(), new PassThroughEncoder());
                    break;
            }

            switch (Input)
            {
                case Message.Small:
                    _hubMessage = new CancelInvocationMessage("123");
                    break;
                case Message.Medium:
                    _hubMessage = new InvocationMessage("123", true, "Target", null, 1, "Foo", 2.0f);
                    break;
                case Message.Large:
                    _hubMessage = new InvocationMessage("123", true, "Target", null, 1, new string('F', 1234), 2.0f);
                    break;
            }

            _binaryInput = GetBytes(_hubMessage);
            _binder = new TestBinder(_hubMessage);
        }

        [Benchmark]
        public void ReadSingleMessage()
        {
            if (!_hubProtocolReaderWriter.ReadMessages(_binaryInput, _binder, out var _))
            {
                throw new InvalidOperationException("Failed to read message");
            }
        }

        [Benchmark]
        public void WriteSingleMessage()
        {
            if (_hubProtocolReaderWriter.WriteMessage(_hubMessage).Length != _binaryInput.Length)
            {
                throw new InvalidOperationException("Failed to write message");
            }
        }

        public enum Protocol
        {
            MsgPack = 0,
            Json = 1
        }

        public enum Message
        {
            Small = 0,
            Medium = 1,
            Large = 2
        }

        private byte[] GetBytes(HubMessage hubMessage)
        {
            return _hubProtocolReaderWriter.WriteMessage(_hubMessage);
        }
    }
}