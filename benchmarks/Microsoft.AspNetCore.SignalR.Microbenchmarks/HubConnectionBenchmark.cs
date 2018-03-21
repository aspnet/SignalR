// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Protocols;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.SignalR.Internal;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
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
            _hubConnection = new HubConnection(new TestConnection(), new JsonHubProtocol(), new NullLoggerFactory());
        }

        [Benchmark]
        public Task StartAsync()
        {
            return _hubConnection.StartAsync();
        }
    }

    public class TestConnection : IConnection
    {
        public Task StartAsync(TransferFormat transferFormat)
        {
            return Task.CompletedTask;
        }

        public Task SendAsync(byte[] data, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            return Task.CompletedTask;
        }

        public Task DisposeAsync()
        {
            return Task.CompletedTask;
        }

        public Task AbortAsync(Exception ex)
        {
            return Task.CompletedTask;
        }

        public IDisposable OnReceived(Func<byte[], object, Task> callback, object state)
        {
            return Task.CompletedTask;
        }

        public event Action<Exception> Closed;
        public IFeatureCollection Features { get; }

        protected virtual void OnClosed(Exception obj)
        {
            Closed?.Invoke(obj);
        }
    }
}