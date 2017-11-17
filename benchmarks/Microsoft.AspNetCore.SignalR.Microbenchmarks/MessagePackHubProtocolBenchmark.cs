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

        [Params(0, 1)]
        public int Input { get; set; }

        [IterationSetup]
        public void Setup()
        {
            switch (Input)
            {
                case 0:
                    _binaryInput = new byte[] { 0x15, 0x95, 0x01, 0xa3, 0x31, 0x32, 0x33, 0xc3, 0xa6, 0x54, 0x61, 0x72, 0x67, 0x65, 0x74, 0x93, 0x01, 0xa3, 0x46, 0x6f, 0x6f, 0x02 };
                    _binder = new TestBinder(new InvocationMessage("123", true, "Target", null, 1, "Foo", 2.0));
                    break;
                case 1:
                    _binaryInput = new byte[] { 0x06, 0x92, 0x05, 0xa3, 0x31, 0x32, 0x33 };
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

//          Method | Input |       Mean |    Error |   StdDev |        Op/s |  Gen 0 | Allocated |
//---------------- |------ |-----------:|---------:|---------:|------------:|-------:|----------:|
// TryParseMessage |     0 | 1,550.8 ns | 31.52 ns | 47.17 ns |   644,823.3 | 0.0095 |     920 B |
// TryParseMessage |     1 |   394.7 ns | 20.92 ns | 31.30 ns | 2,533,817.4 | 0.0049 |     448 B |