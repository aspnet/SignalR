using System;
using System.Buffers;
using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.SignalR.Internal;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Microsoft.AspNetCore.SignalR.Redis.Internal;

namespace Microsoft.AspNetCore.SignalR.Microbenchmarks
{
    public class RedisProtocolBenchmark
    {
        private RedisProtocol _protocol;
        private RedisGroupCommand _groupCommand;
        private object[] _args;
        private string _methodName;
        private IReadOnlyList<string> _excludedIdsSmall;
        private IReadOnlyList<string> _excludedIdsLarge;
        private byte[] _writtenAck;
        private byte[] _writtenGroupCommand;
        private byte[] _writtenInvocationNoExclusions;
        private byte[] _writtenInvocationSmallExclusions;
        private byte[] _writtenInvocationLargeExclusions;

        [GlobalSetup]
        public void GlobalSetup()
        {
            _protocol = new RedisProtocol(new [] {
                new DummyProtocol("protocol1"),
                new DummyProtocol("protocol2")
            });

            _groupCommand = new RedisGroupCommand(id: 42, serverName: "Server", GroupAction.Add, groupName: "group", connectionId: "connection");

            // Because of the DummyProtocol, the args don't really matter
            _args = Array.Empty<object>();
            _methodName = "Method";

            _excludedIdsSmall = GenerateIds(2);
            _excludedIdsLarge = GenerateIds(20);

            _writtenAck = _protocol.WriteAck(42);
            _writtenGroupCommand = _protocol.WriteGroupCommand(_groupCommand);
            _writtenInvocationNoExclusions = _protocol.WriteInvocation(_methodName, _args, null);
            _writtenInvocationSmallExclusions = _protocol.WriteInvocation(_methodName, _args, _excludedIdsSmall);
            _writtenInvocationLargeExclusions = _protocol.WriteInvocation(_methodName, _args, _excludedIdsLarge);
        }

        [Benchmark]
        public void WriteAck()
        {
            _protocol.WriteAck(42);
        }

        [Benchmark]
        public void WriteGroupCommand()
        {
            _protocol.WriteGroupCommand(_groupCommand);
        }

        [Benchmark]
        public void WriteInvocationNoExclusions()
        {
            _protocol.WriteInvocation(_methodName, _args);
        }

        [Benchmark]
        public void WriteInvocationSmallExclusions()
        {
            _protocol.WriteInvocation(_methodName, _args, _excludedIdsSmall);
        }

        [Benchmark]
        public void WriteInvocationLargeExclusions()
        {
            _protocol.WriteInvocation(_methodName, _args, _excludedIdsLarge);
        }

        [Benchmark]
        public void ReadAck()
        {
            _protocol.ReadAck(_writtenAck);
        }

        [Benchmark]
        public void ReadGroupCommand()
        {
            _protocol.ReadGroupCommand(_writtenGroupCommand);
        }

        [Benchmark]
        public void ReadInvocationNoExclusions()
        {
            _protocol.ReadInvocation(_writtenInvocationNoExclusions);
        }

        [Benchmark]
        public void ReadInvocationSmallExclusions()
        {
            _protocol.ReadInvocation(_writtenInvocationSmallExclusions);
        }

        [Benchmark]
        public void ReadInvocationLargeExclusions()
        {
            _protocol.ReadInvocation(_writtenInvocationLargeExclusions);
        }

        private static IReadOnlyList<string> GenerateIds(int count)
        {
            var ids = new string[count];
            for(var i = 0; i < count; i++)
            {
                ids[i] = Guid.NewGuid().ToString("N");
            }
            return ids;
        }

        private class DummyProtocol: IHubProtocol
        {
            private static readonly byte[] _fixedOutput = new byte[] { 0x68, 0x68, 0x6C, 0x6C, 0x6F };

            public string Name { get; }

            public int Version => 1;

            public TransferFormat TransferFormat => TransferFormat.Text;

            public DummyProtocol(string name)
            {
                Name = name;
            }

            public bool IsVersionSupported(int version) => true;

            public bool TryParseMessage(ref ReadOnlySequence<byte> input, IInvocationBinder binder, out HubMessage message)
            {
                throw new NotSupportedException();
            }

            public void WriteMessage(HubMessage message, IBufferWriter<byte> output)
            {
                output.Write(_fixedOutput);
            }

            public byte[] GetBytes(HubMessage message)
            {
                return HubProtocolExtensions.GetBytes(this, message);
            }
        }
    }
}
