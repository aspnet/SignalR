// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Microsoft.AspNetCore.SignalR.Tests;
using Microsoft.AspNetCore.Testing.xunit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.AspNetCore.SignalR.Redis.Tests
{
    [CollectionDefinition(Name)]
    public class EndToEndTestsCollection : ICollectionFixture<RedisServerFixture<Startup>>
    {
        public const string Name = "RedisEndToEndTests";
    }

    [Collection(EndToEndTestsCollection.Name)]
    public class RedisEndToEndTests : LoggedTest
    {
        private readonly RedisServerFixture<Startup> _serverFixture;

        public RedisEndToEndTests(RedisServerFixture<Startup> serverFixture, ITestOutputHelper output) : base(output)
        {
            if (serverFixture == null)
            {
                throw new ArgumentNullException(nameof(serverFixture));
            }

            _serverFixture = serverFixture;
        }

        [ConditionalTheory()]
        [SkipIfDockerNotPresent]
        [MemberData(nameof(TransportTypesAndProtocolTypes))]
        public async Task HubConnectionCanSendAndReceiveMessages(HttpTransportTypes transportType, string protocolName)
        {
            using (StartLog(out var loggerFactory, testName:
                $"{nameof(HubConnectionCanSendAndReceiveMessages)}_{transportType.ToString()}_{protocolName}"))
            {
                var protocol = HubProtocolHelpers.GetHubProtocol(protocolName);

                var connection = CreateConnection(_serverFixture.FirstServer.Url + "/echo", transportType, protocol, loggerFactory);

                await connection.StartAsync().OrTimeout();
                var str = await connection.InvokeAsync<string>("Echo", "Hello, World!").OrTimeout();

                Assert.Equal("Hello, World!", str);

                await connection.DisposeAsync().OrTimeout();
            }
        }

        [ConditionalTheory()]
        [SkipIfDockerNotPresent]
        [MemberData(nameof(TransportTypesAndProtocolTypes))]
        public async Task HubConnectionCanSendAndReceiveGroupMessages(HttpTransportTypes transportType, string protocolName)
        {
            using (StartLog(out var loggerFactory, testName:
                $"{nameof(HubConnectionCanSendAndReceiveGroupMessages)}_{transportType.ToString()}_{protocolName}"))
            {
                var protocol = HubProtocolHelpers.GetHubProtocol(protocolName);

                var connection = CreateConnection(_serverFixture.FirstServer.Url + "/echo", transportType, protocol, loggerFactory);
                var secondConnection = CreateConnection(_serverFixture.SecondServer.Url + "/echo", transportType, protocol, loggerFactory);

                var tcs = new TaskCompletionSource<string>();
                connection.On<string>("Echo", message => tcs.TrySetResult(message));
                var tcs2 = new TaskCompletionSource<string>();
                secondConnection.On<string>("Echo", message => tcs2.TrySetResult(message));

                await secondConnection.StartAsync().OrTimeout();
                await connection.StartAsync().OrTimeout();
                await connection.InvokeAsync("AddSelfToGroup", "Test").OrTimeout();
                await secondConnection.InvokeAsync("AddSelfToGroup", "Test").OrTimeout();
                await connection.InvokeAsync("EchoGroup", "Test", "Hello, World!").OrTimeout();

                Assert.Equal("Hello, World!", await tcs.Task.OrTimeout());
                Assert.Equal("Hello, World!", await tcs2.Task.OrTimeout());

                await connection.DisposeAsync().OrTimeout();
            }
        }

        private static HubConnection CreateConnection(string url, HttpTransportTypes transportType, IHubProtocol protocol, ILoggerFactory loggerFactory)
        {
            return new HubConnectionBuilder()
                .WithHubProtocol(protocol)
                .WithLoggerFactory(loggerFactory)
                .WithUrl(url, transportType)
                .Build();
        }

        private static IEnumerable<HttpTransportTypes> TransportTypes()
        {
            if (TestHelpers.IsWebSocketsSupported())
            {
                yield return HttpTransportTypes.WebSockets;
            }
            yield return HttpTransportTypes.ServerSentEvents;
            yield return HttpTransportTypes.LongPolling;
        }

        public static IEnumerable<object[]> TransportTypesAndProtocolTypes
        {
            get
            {
                foreach (var transport in TransportTypes())
                {
                    yield return new object[] { transport, JsonHubProtocol.ProtocolName };

                    if (transport != HttpTransportTypes.ServerSentEvents)
                    {
                        yield return new object[] { transport, MessagePackHubProtocol.ProtocolName };
                    }
                }
            }
        }
    }
}
