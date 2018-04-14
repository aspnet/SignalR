// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.SignalR.Internal;
using Microsoft.AspNetCore.SignalR.Protocol;
using Microsoft.AspNetCore.SignalR.Redis;
using Microsoft.AspNetCore.SignalR.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Microsoft.AspNetCore.SignalR.Microbenchmarks
{
    public class RedisHubLifetimeManagerBenchmark
    {
        private RedisHubLifetimeManager<TestHub> _manager1;
        private RedisHubLifetimeManager<TestHub> _manager2;
        private TestClient[] _clients;
        private object[] _args;
        private readonly List<string> _excludedIds = new List<string>();
        private readonly List<string> _sendIds = new List<string>();
        private readonly List<string> _groups = new List<string>();
        private readonly List<string> _users = new List<string>();

        private const int ClientCount = 20;

        [Params(2, 20)]
        public int ProtocolCount { get; set; }

        [GlobalSetup]
        public void GlobalSetup()
        {
            var server = new TestRedisServer();
            var logger = NullLogger<RedisHubLifetimeManager<TestHub>>.Instance;
            var protocols = GenerateProtocols(ProtocolCount).ToArray();
            var options = Options.Create(new RedisOptions()
            {
                Factory = _ => Task.FromResult<IConnectionMultiplexer>(new TestConnectionMultiplexer(server))
            });
            var resolver = new DefaultHubProtocolResolver(protocols, NullLogger<DefaultHubProtocolResolver>.Instance);

            _manager1 = new RedisHubLifetimeManager<TestHub>(logger, options, resolver);
            _manager2 = new RedisHubLifetimeManager<TestHub>(logger, options, resolver);

            async Task ConnectClient(TestClient client, IHubProtocol protocol, string userId, string group)
            {
                await _manager2.OnConnectedAsync(HubConnectionContextUtils.Create(client.Connection, protocol, userId));
                await _manager2.AddGroupAsync(client.Connection.ConnectionId, "Everyone");
                await _manager2.AddGroupAsync(client.Connection.ConnectionId, group);
            }

            // Connect clients
            _clients = new TestClient[ClientCount];
            var tasks = new Task[ClientCount];
            for (var i = 0; i < _clients.Length; i++)
            {
                var protocol = protocols[i % ProtocolCount];
                _clients[i] = new TestClient(protocol: protocol);

                string group;
                string user;
                if ((i % 2) == 0)
                {
                    group = "Evens";
                    user = "EvenUser";
                    _excludedIds.Add(_clients[i].Connection.ConnectionId);
                }
                else
                {
                    group = "Odds";
                    user = "OddUser";
                    _sendIds.Add(_clients[i].Connection.ConnectionId);
                }

                tasks[i] = ConnectClient(_clients[i], protocol, user, group);
                _ = ConsumeAsync(_clients[i]);
            }

            Task.WaitAll(tasks);

            _groups.Add("Evens");
            _groups.Add("Odds");
            _users.Add("EvenUser");
            _users.Add("OddUser");

            _args = new object[] {"Foo"};
        }

        private IEnumerable<IHubProtocol> GenerateProtocols(int protocolCount)
        {
            for (var i = 0; i < protocolCount; i++)
            {
                yield return ((i % 2) == 0)
                    ? new WrappedHubProtocol($"json_{i}", new JsonHubProtocol())
                    : new WrappedHubProtocol($"messagepack_{i}", new MessagePackHubProtocol());
            }
        }

        private async Task ConsumeAsync(TestClient testClient)
        {
            while (await testClient.ReadAsync() != null)
            {
                // Just dump the message
            }
        }

        [Benchmark]
        public async Task SendAll()
        {
            await _manager1.SendAllAsync("Test", _args);
        }

        [Benchmark]
        public async Task SendGroup()
        {
            await _manager1.SendGroupAsync("Everyone", "Test", _args);
        }

        [Benchmark]
        public async Task SendUser()
        {
            await _manager1.SendUserAsync("EvenUser", "Test", _args);
        }

        [Benchmark]
        public async Task SendConnection()
        {
            await _manager1.SendConnectionAsync(_clients[0].Connection.ConnectionId, "Test", _args);
        }

        [Benchmark]
        public async Task SendConnections()
        {
            await _manager1.SendConnectionsAsync(_sendIds, "Test", _args);
        }

        [Benchmark]
        public async Task SendAllExcept()
        {
            await _manager1.SendAllExceptAsync("Test", _args, _excludedIds);
        }

        [Benchmark]
        public async Task SendGroupExcept()
        {
            await _manager1.SendGroupExceptAsync("Everyone", "Test", _args, _excludedIds);
        }

        [Benchmark]
        public async Task SendGroups()
        {
            await _manager1.SendGroupsAsync(_groups, "Test", _args);
        }

        [Benchmark]
        public async Task SendUsers()
        {
            await _manager1.SendUsersAsync(_users, "Test", _args);
        }

        public class TestHub : Hub
        {
        }

        private class WrappedHubProtocol : IHubProtocol
        {
            private readonly string _name;
            private readonly IHubProtocol _innerProtocol;

            public string Name => _name;

            public int Version => _innerProtocol.Version;

            public TransferFormat TransferFormat => _innerProtocol.TransferFormat;

            public WrappedHubProtocol(string name, IHubProtocol innerProtocol)
            {
                _name = name;
                _innerProtocol = innerProtocol;
            }

            public bool TryParseMessage(ref ReadOnlySequence<byte> input, IInvocationBinder binder, out HubMessage message)
            {
                return _innerProtocol.TryParseMessage(ref input, binder, out message);
            }

            public void WriteMessage(HubMessage message, IBufferWriter<byte> output)
            {
                _innerProtocol.WriteMessage(message, output);
            }

            public ReadOnlyMemory<byte> GetMessageBytes(HubMessage message)
            {
                return HubProtocolExtensions.GetMessageBytes(this, message);
            }

            public bool IsVersionSupported(int version)
            {
                return _innerProtocol.IsVersionSupported(version);
            }
        }
    }
}
