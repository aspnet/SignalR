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
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.SignalR.Internal;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Microsoft.AspNetCore.SignalR.Microbenchmarks.Shared;
using Microsoft.AspNetCore.Sockets.Client;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.AspNetCore.SignalR.Microbenchmarks
{
    public class HubConnectionBenchmark
    {
        private HubConnection _hubConnection;

        [GlobalSetup]
        public void GlobalSetup()
        {
            var ms = new MemoryStream();
            HandshakeProtocol.WriteResponseMessage(HandshakeResponseMessage.Empty, ms);
            var pipe = new TestDuplexPipe(new ReadResult(new ReadOnlySequence<byte>(ms.ToArray()), false, false));

            var connection = new TestConnection();
            // prevents keep alive time being activated
            connection.Features.Set<IConnectionInherentKeepAliveFeature>(new TestConnectionInherentKeepAliveFeature());
            connection.Transport = pipe;

            _hubConnection = new HubConnection(() => connection, new JsonHubProtocol(), new NullLoggerFactory());
        }

        [Benchmark]
        public async Task StartAsync()
        {
            await _hubConnection.StartAsync();
            await _hubConnection.StopAsync();
        }
    }

    public class TestConnectionInherentKeepAliveFeature : IConnectionInherentKeepAliveFeature
    {
        public TimeSpan KeepAliveInterval { get; } = TimeSpan.Zero;
    }

    public class TestConnection : IConnection
    {
        public Task StartAsync()
        {
            throw new NotImplementedException();
        }

        public Task StartAsync(TransferFormat transferFormat)
        {
            return Task.CompletedTask;
        }

        public Task DisposeAsync()
        {
            return Task.CompletedTask;
        }

        public IDuplexPipe Transport { get; set; }

        public IFeatureCollection Features { get; } = new FeatureCollection();
    }
}