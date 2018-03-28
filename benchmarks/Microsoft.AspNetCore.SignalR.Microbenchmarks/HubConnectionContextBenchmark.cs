// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.SignalR.Core;
using Microsoft.AspNetCore.SignalR.Internal;
using Microsoft.AspNetCore.SignalR.Internal.Formatters;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Microsoft.AspNetCore.SignalR.Microbenchmarks.Shared;
using Microsoft.AspNetCore.Sockets.Client;
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

        [GlobalSetup]
        public void GlobalSetup()
        {
            var memoryBufferWriter = new MemoryBufferWriter();
            HandshakeProtocol.WriteRequestMessage(new HandshakeRequestMessage("json", 1), memoryBufferWriter);
            var pipe = new TestDuplexPipe(new ReadResult(new ReadOnlySequence<byte>(memoryBufferWriter.ToArray()), false, false));

            var connection = new DefaultConnectionContext(Guid.NewGuid().ToString(), pipe, pipe);
            _hubConnectionContext = new HubConnectionContext(connection, Timeout.InfiniteTimeSpan, NullLoggerFactory.Instance);

            _successHubProtocolResolver = new TestHubProtocolResolver(new JsonHubProtocol());
            _failureHubProtocolResolver = new TestHubProtocolResolver(null);
            _userIdProvider = new TestUserIdProvider();
            _supportedProtocols = new List<string> {"json"};
        }

        [Benchmark]
        public async Task SuccessHandshakeAsync()
        {
            await _hubConnectionContext.HandshakeAsync(TimeSpan.FromSeconds(5), _supportedProtocols, _successHubProtocolResolver, _userIdProvider);
        }

        [Benchmark]
        public async Task ErrorHandshakeAsync()
        {
            await _hubConnectionContext.HandshakeAsync(TimeSpan.FromSeconds(5), _supportedProtocols, _failureHubProtocolResolver, _userIdProvider);
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

        public TestHubProtocolResolver(IHubProtocol instance)
        {
            _instance = instance;
        }

        public IHubProtocol GetProtocol(string protocolName, IList<string> supportedProtocols)
        {
            return _instance;
        }
    }
}