// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading.Tasks;
using System.Threading.Tasks.Channels;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Microsoft.AspNetCore.SignalR.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.AspNetCore.SignalR.Redis.Tests
{
    public class RedisHubLifetimeManagerTests
    {
        [Fact]
        public async Task InvokeAllAsyncWritesToAllConnectionsOutput()
        {
            using (var client1 = new TestClient())
            using (var client2 = new TestClient())
            {
                var output1 = Channel.CreateUnbounded<HubMessage>();
                var output2 = Channel.CreateUnbounded<HubMessage>();

                var manager = new RedisHubLifetimeManager<MyHub>(new LoggerFactory().CreateLogger<RedisHubLifetimeManager<MyHub>>(), Options.Create(new RedisOptions()
                {
                    Factory = t => new TestConnectionMultiplexer()
                }));
                var connection1 = new HubConnectionContext(output1, client1.Connection);
                var connection2 = new HubConnectionContext(output2, client2.Connection);

                await manager.OnConnectedAsync(connection1);
                await manager.OnConnectedAsync(connection2);

                await manager.InvokeAllAsync("Hello", new object[] { "World" });

                AssertMessage(output1);
                AssertMessage(output2);
            }
        }

        [Fact]
        public async Task InvokeAllAsyncDoesNotWriteToDisconnectedConnectionsOutput()
        {
            using (var client1 = new TestClient())
            using (var client2 = new TestClient())
            {
                var output1 = Channel.CreateUnbounded<HubMessage>();
                var output2 = Channel.CreateUnbounded<HubMessage>();

                var manager = new RedisHubLifetimeManager<MyHub>(new LoggerFactory().CreateLogger<RedisHubLifetimeManager<MyHub>>(), Options.Create(new RedisOptions()
                {
                    Factory = t => new TestConnectionMultiplexer()
                }));
                var connection1 = new HubConnectionContext(output1, client1.Connection);
                var connection2 = new HubConnectionContext(output2, client2.Connection);

                await manager.OnConnectedAsync(connection1);
                await manager.OnConnectedAsync(connection2);

                await manager.OnDisconnectedAsync(connection2);

                await manager.InvokeAllAsync("Hello", new object[] { "World" });

                AssertMessage(output1);

                Assert.False(output2.In.TryRead(out var item));
            }
        }

        [Fact]
        public async Task InvokeGroupAsyncWritesToAllConnectionsInGroupOutput()
        {
            using (var client1 = new TestClient())
            using (var client2 = new TestClient())
            {
                var output1 = Channel.CreateUnbounded<HubMessage>();
                var output2 = Channel.CreateUnbounded<HubMessage>();

                var manager = new RedisHubLifetimeManager<MyHub>(new LoggerFactory().CreateLogger<RedisHubLifetimeManager<MyHub>>(), Options.Create(new RedisOptions()
                {
                    Factory = t => new TestConnectionMultiplexer()
                }));
                var connection1 = new HubConnectionContext(output1, client1.Connection);
                var connection2 = new HubConnectionContext(output2, client2.Connection);

                await manager.OnConnectedAsync(connection1);
                await manager.OnConnectedAsync(connection2);

                await manager.AddGroupAsync(connection1.ConnectionId, "gunit");

                await manager.InvokeGroupAsync("gunit", "Hello", new object[] { "World" });

                AssertMessage(output1);

                Assert.False(output2.In.TryRead(out var item));
            }
        }

        [Fact]
        public async Task InvokeConnectionAsyncWritesToConnectionOutput()
        {
            using (var client = new TestClient())
            {
                var output = Channel.CreateUnbounded<HubMessage>();
                var manager = new RedisHubLifetimeManager<MyHub>(new LoggerFactory().CreateLogger<RedisHubLifetimeManager<MyHub>>(), Options.Create(new RedisOptions()
                {
                    Factory = t => new TestConnectionMultiplexer()
                }));
                var connection = new HubConnectionContext(output, client.Connection);

                await manager.OnConnectedAsync(connection);

                await manager.InvokeConnectionAsync(connection.ConnectionId, "Hello", new object[] { "World" });

                AssertMessage(output);
            }
        }

        [Fact]
        public async Task InvokeConnectionAsyncOnNonExistentConnectionNoops()
        {
            var manager = new RedisHubLifetimeManager<MyHub>(new LoggerFactory().CreateLogger<RedisHubLifetimeManager<MyHub>>(), Options.Create(new RedisOptions()
            {
                Factory = t => new TestConnectionMultiplexer()
            }));
            await manager.InvokeConnectionAsync("NotARealConnectionId", "Hello", new object[] { "World" });
        }

        //[Fact]
        //public async Task AddGroupOnNonExistentConnectionNoops()
        //{
        //    var manager = new RedisHubLifetimeManager<MyHub>(new LoggerFactory().CreateLogger<RedisHubLifetimeManager<MyHub>>(), Options.Create(new RedisOptions()
        //    {
        //        Factory = t => new TestConnectionMultiplexer()
        //    }));
        //    await manager.AddGroupAsync("NotARealConnectionId", "MyGroup");
        //}

        //[Fact]
        //public async Task RemoveGroupOnNonExistentConnectionNoops()
        //{
        //    var manager = new RedisHubLifetimeManager<MyHub>(new LoggerFactory().CreateLogger<RedisHubLifetimeManager<MyHub>>(), Options.Create(new RedisOptions()
        //    {
        //        Factory = t => new TestConnectionMultiplexer()
        //    }));
        //    await manager.RemoveGroupAsync("NotARealConnectionId", "MyGroup");
        //}

        [Fact]
        public async Task InvokeAllAsyncWithMultipleServersWritesToAllConnectionsOutput()
        {
            var manager1 = new RedisHubLifetimeManager<MyHub>(new LoggerFactory().CreateLogger<RedisHubLifetimeManager<MyHub>>(), Options.Create(new RedisOptions()
            {
                Factory = t => new TestConnectionMultiplexer()
            }));
            var manager2 = new RedisHubLifetimeManager<MyHub>(new LoggerFactory().CreateLogger<RedisHubLifetimeManager<MyHub>>(), Options.Create(new RedisOptions()
            {
                Factory = t => new TestConnectionMultiplexer()
            }));

            using (var client1 = new TestClient())
            using (var client2 = new TestClient())
            {
                var output1 = Channel.CreateUnbounded<HubMessage>();
                var output2 = Channel.CreateUnbounded<HubMessage>();

                var connection1 = new HubConnectionContext(output1, client1.Connection);
                var connection2 = new HubConnectionContext(output2, client2.Connection);

                await manager1.OnConnectedAsync(connection1);
                await manager2.OnConnectedAsync(connection2);

                await manager1.InvokeAllAsync("Hello", new object[] { "World" });

                AssertMessage(output1);
                AssertMessage(output2);
            }
        }

        [Fact]
        public async Task InvokeAllAsyncWithMultipleServersDoesNotWriteToDisconnectedConnectionsOutput()
        {
            var manager1 = new RedisHubLifetimeManager<MyHub>(new LoggerFactory().CreateLogger<RedisHubLifetimeManager<MyHub>>(), Options.Create(new RedisOptions()
            {
                Factory = t => new TestConnectionMultiplexer()
            }));
            var manager2 = new RedisHubLifetimeManager<MyHub>(new LoggerFactory().CreateLogger<RedisHubLifetimeManager<MyHub>>(), Options.Create(new RedisOptions()
            {
                Factory = t => new TestConnectionMultiplexer()
            }));

            using (var client1 = new TestClient())
            using (var client2 = new TestClient())
            {
                var output1 = Channel.CreateUnbounded<HubMessage>();
                var output2 = Channel.CreateUnbounded<HubMessage>();

                var connection1 = new HubConnectionContext(output1, client1.Connection);
                var connection2 = new HubConnectionContext(output2, client2.Connection);

                await manager1.OnConnectedAsync(connection1);
                await manager2.OnConnectedAsync(connection2);

                await manager2.OnDisconnectedAsync(connection2);

                await manager2.InvokeAllAsync("Hello", new object[] { "World" });

                AssertMessage(output1);

                Assert.False(output2.In.TryRead(out var item));
            }
        }

        [Fact]
        public async Task InvokeConnectionAsyncOnServerWithoutConnectionWritesOutputToConnection()
        {
            var manager1 = new RedisHubLifetimeManager<MyHub>(new LoggerFactory().CreateLogger<RedisHubLifetimeManager<MyHub>>(), Options.Create(new RedisOptions()
            {
                Factory = t => new TestConnectionMultiplexer()
            }));
            var manager2 = new RedisHubLifetimeManager<MyHub>(new LoggerFactory().CreateLogger<RedisHubLifetimeManager<MyHub>>(), Options.Create(new RedisOptions()
            {
                Factory = t => new TestConnectionMultiplexer()
            }));

            using (var client = new TestClient())
            {
                var output = Channel.CreateUnbounded<HubMessage>();

                var connection = new HubConnectionContext(output, client.Connection);

                await manager1.OnConnectedAsync(connection);

                await manager2.InvokeConnectionAsync(connection.ConnectionId, "Hello", new object[] { "World" });

                AssertMessage(output);
            }
        }

        [Fact]
        public async Task InvokeGroupAsyncOnServerWithoutConnectionWritesOutputToGroupConnection()
        {
            var manager1 = new RedisHubLifetimeManager<MyHub>(new LoggerFactory().CreateLogger<RedisHubLifetimeManager<MyHub>>(), Options.Create(new RedisOptions()
            {
                Factory = t => new TestConnectionMultiplexer()
            }));
            var manager2 = new RedisHubLifetimeManager<MyHub>(new LoggerFactory().CreateLogger<RedisHubLifetimeManager<MyHub>>(), Options.Create(new RedisOptions()
            {
                Factory = t => new TestConnectionMultiplexer()
            }));

            using (var client = new TestClient())
            {
                var output = Channel.CreateUnbounded<HubMessage>();

                var connection = new HubConnectionContext(output, client.Connection);

                await manager1.OnConnectedAsync(connection);

                await manager1.AddGroupAsync(connection.ConnectionId, "name");

                await manager2.InvokeGroupAsync("name", "Hello", new object[] { "World" });

                AssertMessage(output);
            }
        }

        private void AssertMessage(Channel<HubMessage> channel)
        {
            Assert.True(channel.In.TryRead(out var item));
            var message = item as InvocationMessage;
            Assert.NotNull(message);
            Assert.Equal("Hello", message.Target);
            Assert.Single(message.Arguments);
            Assert.Equal("World", (string)message.Arguments[0]);
        }

        private class MyHub : Hub
        {

        }
    }
}
