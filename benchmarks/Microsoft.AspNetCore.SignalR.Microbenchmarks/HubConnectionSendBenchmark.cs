// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.SignalR.Internal;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Microsoft.AspNetCore.SignalR.Microbenchmarks.Shared;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.AspNetCore.SignalR.Microbenchmarks
{
    public class HubConnectionSendBenchmark
    {
        private HubConnection _hubConnection;
        private TestDuplexPipe _pipe;
        private TaskCompletionSource<ReadResult> _tcs;

        [GlobalSetup]
        public void GlobalSetup()
        {
            var ms = new MemoryBufferWriter();
            HandshakeProtocol.WriteResponseMessage(HandshakeResponseMessage.Empty, ms);
            var handshakeResponseResult = new ReadResult(new ReadOnlySequence<byte>(ms.ToArray()), false, false);
                
            _pipe = new TestDuplexPipe();
            _pipe.AddReadResult(new ValueTask<ReadResult>(handshakeResponseResult));

            _tcs = new TaskCompletionSource<ReadResult>();
            _pipe.AddReadResult(new ValueTask<ReadResult>(_tcs.Task));

            var connection = new TestConnection();
            // prevents keep alive time being activated
            connection.Features.Set<IConnectionInherentKeepAliveFeature>(new TestConnectionInherentKeepAliveFeature());
            connection.Transport = _pipe;

            _hubConnection = new HubConnection(() => connection, new JsonHubProtocol(), new NullLoggerFactory());
            _hubConnection.StartAsync().GetAwaiter().GetResult();
        }

        [GlobalCleanup]
        public void GlobalCleanup()
        {
            _tcs.SetResult(new ReadResult(default, false, true));
            _hubConnection.StopAsync().GetAwaiter().GetResult();
        }

        [Benchmark]
        public Task SendAsync()
        {
            return _hubConnection.SendAsync("Dummy", Array.Empty<object>());
        }
    }
}