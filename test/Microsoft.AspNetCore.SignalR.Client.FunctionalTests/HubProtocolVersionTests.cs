// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Http.Connections.Client;
using Microsoft.AspNetCore.SignalR.Protocol;
using Microsoft.AspNetCore.SignalR.Tests;
using Microsoft.AspNetCore.Testing.xunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.AspNetCore.SignalR.Client.FunctionalTests
{
    // Disable running server tests in parallel so server logs can accurately be captured per test
    [CollectionDefinition(Name, DisableParallelization = true)]
    public class HubProtocolVersionTestsCollection : ICollectionFixture<ServerFixture<VersionStartup>>
    {
        public const string Name = nameof(HubProtocolVersionTestsCollection);
    }

    [Collection(HubProtocolVersionTestsCollection.Name)]
    public class HubProtocolVersionTests : VerifiableServerLoggedTest
    {
        public HubProtocolVersionTests(ServerFixture<VersionStartup> serverFixture, ITestOutputHelper output) : base(serverFixture, output)
        {
        }

        [Theory]
        [MemberData(nameof(TransportTypes))]
        public async Task ClientUsingOriginalProtocol(HttpTransportType transportType)
        {
            using (StartVerifiableLog(out var loggerFactory, $"{nameof(ClientUsingOriginalProtocol)}_{transportType}"))
            {
                var connectionBuilder = new HubConnectionBuilder()
                    .WithLoggerFactory(loggerFactory)
                    .WithUrl(ServerFixture.Url + "/version", transportType);
                connectionBuilder.Services.AddSingleton(new JsonHubProtocol());

                var connection = connectionBuilder.Build();

                try
                {
                    await connection.StartAsync().OrTimeout();

                    var result = await connection.InvokeAsync<string>(nameof(VersionHub.Echo)).OrTimeout();

                    Assert.Equal("Hello World!", result);
                }
                catch (Exception ex)
                {
                    loggerFactory.CreateLogger<HubConnectionTests>().LogError(ex, "{ExceptionType} from test", ex.GetType().FullName);
                    throw;
                }
                finally
                {
                    await connection.DisposeAsync().OrTimeout();
                }
            }
        }

        //public static IEnumerable<object[]> HubProtocolsAndTransports
        //{
        //    get
        //    {
        //        foreach (var transport in TransportTypes().SelectMany(t => t).Cast<HttpTransportType>())
        //        {
        //            yield return new object[] {transport};
        //        }
        //    }
        //}

        public static Dictionary<string, IHubProtocol> HubProtocols =>
            new Dictionary<string, IHubProtocol>
            {
                { "json", new JsonHubProtocol() },
                { "messagepack", new MessagePackHubProtocol() },
            };

        public static IEnumerable<object[]> TransportTypes()
        {
            if (TestHelpers.IsWebSocketsSupported())
            {
                yield return new object[] { HttpTransportType.WebSockets };
            }
            yield return new object[] { HttpTransportType.ServerSentEvents };
            yield return new object[] { HttpTransportType.LongPolling };
        }
    }
}
