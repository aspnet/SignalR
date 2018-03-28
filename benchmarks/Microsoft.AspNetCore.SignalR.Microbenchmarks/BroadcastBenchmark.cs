// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.AspNetCore.SignalR.Microbenchmarks
{
    public class BroadcastBenchmark
    {
        private DefaultHubLifetimeManager<Hub> _hubLifetimeManager;
        private HubContext<Hub> _hubContext;
        private List<DefaultConnectionContext> _connections;
        private List<HubConnectionContext> _hubConnections;

        [Params(1, 10, 1000)]
        public int Connections;

        [Params("json", "msgpack")]
        public string Protocol;

        [GlobalSetup]
        public void GlobalSetup()
        {
            _hubLifetimeManager = new DefaultHubLifetimeManager<Hub>(NullLogger<DefaultHubLifetimeManager<Hub>>.Instance);

            IHubProtocol protocol;

            if (Protocol == "json")
            {
                protocol = new JsonHubProtocol();
            }
            else
            {
                protocol = new MessagePackHubProtocol();
            }

            _hubConnections = new List<HubConnectionContext>(Connections);
            _connections = new List<DefaultConnectionContext>(Connections);
            var options = new PipeOptions();
            for (var i = 0; i < Connections; ++i)
            {
                var pair = DuplexPipe.CreateConnectionPair(options, options);
                var connection = new DefaultConnectionContext(Guid.NewGuid().ToString(), pair.Application, pair.Transport);
                var hubConnection = new HubConnectionContext(connection, Timeout.InfiniteTimeSpan, NullLoggerFactory.Instance);
                hubConnection.Protocol = protocol;
                _hubLifetimeManager.OnConnectedAsync(hubConnection).GetAwaiter().GetResult();
                _connections.Add(connection);
                _hubConnections.Add(hubConnection);
                //_ = ConsumeAsync(connection.Application);
            }

            _hubContext = new HubContext<Hub>(_hubLifetimeManager);

            _hubContext.Clients.All.SendAsync("Method").GetAwaiter().GetResult();
        }

        [GlobalCleanup]
        public void GlobalCleanup()
        {
            foreach (var connection in _connections)
            {
                connection.Transport.Output.Complete();
                connection.Transport.Input.CancelPendingRead();
                connection.Transport.Input.Complete();

                connection.Application.Output.Complete();
                connection.Application.Input.CancelPendingRead();
                connection.Application.Input.Complete();
            }
            _connections.Clear();

            foreach (var hubConnection in _hubConnections)
            {
                _hubLifetimeManager.OnDisconnectedAsync(hubConnection).GetAwaiter().GetResult();
            }
            _hubConnections.Clear();
        }

        [Benchmark]
        public Task SendAsyncAll()
        {
            return _hubContext.Clients.All.SendAsync("Method");
        }

        [IterationCleanup]
        // Consume the data written to the transports
        public void ConsumeAsync()
        {
            foreach (var connection in _connections)
            {
                var readResult = connection.Application.Input.ReadAsync();
                if (readResult.IsCompleted)
                {
                    var buffer = readResult.Result.Buffer;

                    if (!buffer.IsEmpty)
                    {
                        connection.Application.Input.AdvanceTo(buffer.End);
                    }
                }
            }
        }
    }
}
