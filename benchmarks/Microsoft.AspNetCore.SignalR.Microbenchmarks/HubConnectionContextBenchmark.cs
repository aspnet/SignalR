// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.SignalR.Internal;
using Microsoft.AspNetCore.Internal;
using Microsoft.AspNetCore.SignalR.Protocol;
using Microsoft.AspNetCore.SignalR.Microbenchmarks.Shared;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.AspNetCore.SignalR.Microbenchmarks
{
    public class HubConnectionContextBenchmark
    {
        private HubConnectionContext _hubConnectionContext;
        private TestHubProtocolResolver _successHubProtocolResolver;
        private TestHubProtocolResolver _failureHubProtocolResolver;
        private TestUserIdProvider _userIdProvider;
        private List<string> _supportedProtocols;
        private TestDuplexPipe _pipe;
        private ReadResult _handshakeRequestResult;

        [GlobalSetup]
        public void GlobalSetup()
        {
            var memoryBufferWriter = MemoryBufferWriter.Get();
            try
            {
                HandshakeProtocol.WriteRequestMessage(new HandshakeRequestMessage("json", 1), memoryBufferWriter);
                _handshakeRequestResult = new ReadResult(new ReadOnlySequence<byte>(memoryBufferWriter.ToArray()), false, false);
            }
            finally
            {
                MemoryBufferWriter.Return(memoryBufferWriter);
            }

            _pipe = new TestDuplexPipe();

            var connection = new DefaultConnectionContext(Guid.NewGuid().ToString(), _pipe, _pipe);
            _hubConnectionContext = new HubConnectionContext(connection, Timeout.InfiniteTimeSpan, NullLoggerFactory.Instance);

            _successHubProtocolResolver = new TestHubProtocolResolver(new JsonHubProtocol());
            _failureHubProtocolResolver = new TestHubProtocolResolver(null);
            _userIdProvider = new TestUserIdProvider();
            _supportedProtocols = new List<string> { "json" };
        }

        [Benchmark]
        public async Task SuccessHandshakeAsync()
        {
            _pipe.AddReadResult(new ValueTask<ReadResult>(_handshakeRequestResult));

            await _hubConnectionContext.HandshakeAsync(TimeSpan.FromSeconds(5), _supportedProtocols, _successHubProtocolResolver,
                _userIdProvider, enableDetailedErrors: true);
        }

        [Benchmark]
        public async Task ErrorHandshakeAsync()
        {
            _pipe.AddReadResult(new ValueTask<ReadResult>(_handshakeRequestResult));

            await _hubConnectionContext.HandshakeAsync(TimeSpan.FromSeconds(5), _supportedProtocols, _failureHubProtocolResolver,
                _userIdProvider, enableDetailedErrors: true);
        }
    }

    public class TestUserIdProvider : IUserIdProvider
    {
        public string GetUserId(HubConnectionContext connection)
        {
            return "UserId!";
        }
    }

    public class TestHubProtocolResolver : IHubProtocolResolver
    {
        private readonly IHubProtocol _instance;

        public IReadOnlyList<IHubProtocol> AllProtocols { get; }

        public TestHubProtocolResolver(IHubProtocol instance)
        {
            AllProtocols = new[] { instance };
            _instance = instance;
        }

        public IHubProtocol GetProtocol(string protocolName, IReadOnlyList<string> supportedProtocols)
        {
            return _instance;
        }
    }
}
