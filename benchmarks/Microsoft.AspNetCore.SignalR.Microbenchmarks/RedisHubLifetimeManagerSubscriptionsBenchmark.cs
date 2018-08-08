// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.SignalR.Internal;
using Microsoft.AspNetCore.SignalR.Protocol;
using Microsoft.AspNetCore.SignalR.Redis;
using Microsoft.AspNetCore.SignalR.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Microsoft.AspNetCore.SignalR.Microbenchmarks
{
    public class RedisHubLifetimeManagerSubscriptionsBenchmark
    {
        private RedisHubLifetimeManager<TestHub> _manager;
        private HubConnectionContext[] _connections;

        public int ClientCount { get; set; } = 30;

        [Params(1, 20)]
        public int UserCount { get; set; }

        [Params(1, 15)]
        public int GroupCount { get; set; }

        [GlobalSetup]
        public void GlobalSetup()
        {
            var server = new TestRedisServer(1);
            var logger = NullLogger<RedisHubLifetimeManager<TestHub>>.Instance;
            var protocol = new JsonHubProtocol();
            var options = Options.Create(new RedisOptions()
            {
                ConnectionFactory = _ => Task.FromResult<IConnectionMultiplexer>(new TestConnectionMultiplexer(server))
            });
            var resolver = new DefaultHubProtocolResolver(new[] { protocol }, NullLogger<DefaultHubProtocolResolver>.Instance);

            _manager = new RedisHubLifetimeManager<TestHub>(logger, options, resolver);

            // Create connections
            _connections = new HubConnectionContext[ClientCount];
            var tasks = new Task[ClientCount];
            for (var i = 0; i < _connections.Length; i++)
            {
                _connections[i] = HubConnectionContextUtils.Create(new TestClient(protocol: protocol).Connection, protocol, $"User{i % UserCount}");
            }
        }

        [Benchmark]
        public async Task JoinLeave()
        {
            var tasks = new Task[ClientCount];
            for (var i = 0; i < _connections.Length; i++)
            {
                tasks[i] = JoinLeave(i);
            }
            await Task.WhenAll(tasks);
        }

        private Task JoinLeave(int i)
        {
            return Task.Run(async () => {
                await _manager.OnConnectedAsync(_connections[i]);
                await _manager.AddToGroupAsync(_connections[i].ConnectionId, $"Group{i % GroupCount}");
                await _manager.RemoveFromGroupAsync(_connections[i].ConnectionId, $"Group{i % GroupCount}");
                await _manager.OnDisconnectedAsync(_connections[i]);
            });
        }

        public class TestHub : Hub
        {
        }
    }
}
