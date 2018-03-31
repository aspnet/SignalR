using System;
using System.Buffers;
using System.IO;
using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Http.Connections.Client.Internal;
using Microsoft.AspNetCore.Http.Connections.Internal;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;

namespace Microsoft.AspNetCore.SignalR.Microbenchmarks
{
    public class ServerSentEventsTransportBenchmark
    {
        private byte[] _data;

        [Params(Message.NoArguments, Message.FewArguments, Message.ManyArguments, Message.LargeArguments)]
        public Message Input { get; set; }

        [GlobalSetup]
        public void GlobalSetup()
        {
            var hubProtocol = new JsonHubProtocol();
            HubMessage hubMessage = null;
            switch (Input)
            {
                case Message.NoArguments:
                    hubMessage = new InvocationMessage(target: "Target", argumentBindingException: null);
                    break;
                case Message.FewArguments:
                    hubMessage = new InvocationMessage(target: "Target", argumentBindingException: null, 1, "Foo", 2.0f);
                    break;
                case Message.ManyArguments:
                    hubMessage = new InvocationMessage(target: "Target", argumentBindingException: null, 1, "string", 2.0f, true, (byte)9, new int[] { 5, 4, 3, 2, 1 }, 'c', 123456789101112L);
                    break;
                case Message.LargeArguments:
                    hubMessage = new InvocationMessage(target: "Target", argumentBindingException: null, new string('F', 10240), new string('B', 10240));
                    break;
            }

            var buffer = hubProtocol.WriteToArray(hubMessage);
            var ms = new MemoryStream();
            ServerSentEventsMessageFormatter.WriteMessage(buffer, ms);
            _data = ms.ToArray();
        }

        [Benchmark]
        public void ParseMessage()
        {
            var parser = new ServerSentEventsMessageParser();
            var buffer = new ReadOnlySequence<byte>(_data);

            if (parser.ParseMessage(buffer, out var consumed, out var examined, out var message) != ServerSentEventsMessageParser.ParseResult.Completed)
            {
                throw new InvalidOperationException("Parse failed!");
            }
        }

        public enum Message
        {
            NoArguments = 0,
            FewArguments = 1,
            ManyArguments = 2,
            LargeArguments = 3
        }
    }
}
