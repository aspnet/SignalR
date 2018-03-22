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
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Protocols;
using Microsoft.AspNetCore.Protocols.Features;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.SignalR.Core;
using Microsoft.AspNetCore.SignalR.Internal;
using Microsoft.AspNetCore.SignalR.Internal.Formatters;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Microsoft.AspNetCore.Sockets.Client;
using Microsoft.Extensions.Logging.Abstractions;
using DefaultConnectionContext = Microsoft.AspNetCore.Sockets.DefaultConnectionContext;

namespace Microsoft.AspNetCore.SignalR.Microbenchmarks
{
    public class HubConnectionContextBenchmark
    {
        private HubConnectionContext _hubConnectionContext;
        private TestHubProtocolResolver _hubProtocolResolver;
        private TestUserIdProvider _userIdProvider;
        private List<string> _supportedProtocols;

        [GlobalSetup]
        public void GlobalSetup()
        {
            var handshakeRequestData = Encoding.UTF8.GetBytes("{'protocol':'json'}" + (char)TextMessageFormatter.RecordSeparator);
            var pipe = new TestDuplexPipe(new ReadResult(new ReadOnlySequence<byte>(handshakeRequestData), false, false));

            var connection = new DefaultConnectionContext(Guid.NewGuid().ToString(), pipe, pipe);
            _hubConnectionContext = new HubConnectionContext(connection, Timeout.InfiniteTimeSpan, NullLoggerFactory.Instance);

            _hubProtocolResolver = new TestHubProtocolResolver();
            _userIdProvider = new TestUserIdProvider();
            _supportedProtocols = new List<string> {"json"};
        }

        [Benchmark]
        public async Task HandshakeAsync()
        {
            await _hubConnectionContext.HandshakeAsync(TimeSpan.FromSeconds(5), _supportedProtocols, _hubProtocolResolver, _userIdProvider);
        }
    }

    public class TestDuplexPipe : IDuplexPipe
    {
        public PipeReader Input { get; }
        public PipeWriter Output { get; }

        public TestDuplexPipe(ReadResult readResult = default)
        {
            Input = new TestPipeReader(readResult);
            Output = new TestPipeWriter();
        }
    }

    public class TestPipeReader : PipeReader
    {
        private readonly ReadResult _readResult;

        public TestPipeReader(ReadResult readResult = default)
        {
            _readResult = readResult;
        }

        public override void AdvanceTo(SequencePosition consumed)
        {
        }

        public override void AdvanceTo(SequencePosition consumed, SequencePosition examined)
        {
        }

        public override void CancelPendingRead()
        {
            throw new NotImplementedException();
        }

        public override void Complete(Exception exception = null)
        {
            throw new NotImplementedException();
        }

        public override void OnWriterCompleted(Action<Exception, object> callback, object state)
        {
            throw new NotImplementedException();
        }

        public override ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = new CancellationToken())
        {
            return new ValueTask<ReadResult>(_readResult);
        }

        public override bool TryRead(out ReadResult result)
        {
            throw new NotImplementedException();
        }
    }

    public class TestPipeWriter : PipeWriter
    {
        private readonly byte[] _buffer = new byte[100];

        public override void Advance(int bytes)
        {
        }

        public override Memory<byte> GetMemory(int sizeHint = 0)
        {
            return _buffer;
        }

        public override Span<byte> GetSpan(int sizeHint = 0)
        {
            return _buffer;
        }

        public override void OnReaderCompleted(Action<Exception, object> callback, object state)
        {
            throw new NotImplementedException();
        }

        public override void CancelPendingFlush()
        {
            throw new NotImplementedException();
        }

        public override void Complete(Exception exception = null)
        {
            throw new NotImplementedException();
        }

        public override ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken = new CancellationToken())
        {
            return default;
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
        private readonly IHubProtocol _instance = new JsonHubProtocol();

        public IHubProtocol GetProtocol(string protocolName, IList<string> supportedProtocols, HubConnectionContext connection)
        {
            return _instance;
        }
    }
}