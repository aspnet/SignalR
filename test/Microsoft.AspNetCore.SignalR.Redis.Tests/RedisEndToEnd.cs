// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Microsoft.AspNetCore.SignalR.Tests.Common;
using Microsoft.AspNetCore.Sockets;
using Microsoft.AspNetCore.Sockets.Client;
using Microsoft.AspNetCore.Sockets.Features;
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

        [ConditionalTheory]
        [SkipIfDockerNotPresent]
        [MemberData(nameof(TransportTypesAndTransferModes))]
        public async Task HubConnectionCanSendAndReceiveMessages(TransportType transportType, TransferMode requestedTransferMode)
        {
            using (StartLog(out var loggerFactory, testName:
                $"{nameof(HubConnectionCanSendAndReceiveMessages)}_{transportType.ToString()}_{requestedTransferMode.ToString()}"))
            {
                var logger = loggerFactory.CreateLogger<RedisEndToEndTests>();
                var httpConnection = new HttpConnection(new Uri(_serverFixture.BaseUrl + "/echo"), transportType, loggerFactory);
                httpConnection.Features.Set<ITransferModeFeature>(
                    new TransferModeFeature { TransferMode = requestedTransferMode });
                var connection = new HubConnection(httpConnection, new JsonHubProtocol(), loggerFactory);

                await connection.StartAsync();
                var str = await connection.InvokeAsync<string>("Echo", "Hello world");
                await connection.DisposeAsync();

                Assert.Equal("Hello world", str);
            }
        }

        public static IEnumerable<object[]> TransportTypes
        {
            get
            {
                if (TestHelpers.IsWebSocketsSupported())
                {
                    yield return new object[] { TransportType.WebSockets };
                }
                yield return new object[] { TransportType.ServerSentEvents };
                yield return new object[] { TransportType.LongPolling };
            }
        }

        public static IEnumerable<object[]> TransportTypesAndTransferModes
        {
            get
            {
                foreach (var transport in TransportTypes)
                {
                    yield return new object[] { transport[0], TransferMode.Text };
                    yield return new object[] { transport[0], TransferMode.Binary };
                }
            }
        }
    }
}
