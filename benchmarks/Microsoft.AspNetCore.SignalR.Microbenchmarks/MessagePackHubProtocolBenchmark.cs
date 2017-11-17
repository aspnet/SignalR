using System;
using System.Text;
using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;

namespace Microsoft.AspNetCore.SignalR.Microbenchmarks
{
    [Config(typeof(CoreConfig))]
    public class MessagePackHubProtocolBenchmark
    {
        private static readonly MessagePackHubProtocol HubProtocol = new MessagePackHubProtocol();

        private byte[] _binaryInput;
        private TestBinder _binder;

        [Params(0)]
        public int Input { get; set; }

        [IterationSetup]
        public void Setup()
        {
            switch (Input)
            {
                case 0:
                    _binaryInput = new byte[] { 0x95, 0x01, 0xa3, 0x31, 0x32, 0x33, 0xc3, 0xa6, 0x54, 0x61, 0x72, 0x67, 0x65, 0x74, 0x93, 0x01, 0xa3, 0x46, 0x6f, 0x6f, 0x02 };
                    _binder = new TestBinder(new InvocationMessage("123", true, "Target", null, 1, "Foo", 2));
                    break;
                case 1:
                    _binaryInput = Encoding.UTF8.GetBytes("{\"invocationId\":\"123\",\"type\":5}" + Encoding.UTF8.GetString(new[] { (byte)0x1e }));
                    _binder = new TestBinder(new CancelInvocationMessage("123"));
                    break;
            }
        }

        [Benchmark]
        public void TryParseMessage()
        {
            if (!HubProtocol.TryParseMessages(_binaryInput, _binder, out var _))
            {
                throw new InvalidOperationException("Failed to parse");
            }
        }
    }
}
