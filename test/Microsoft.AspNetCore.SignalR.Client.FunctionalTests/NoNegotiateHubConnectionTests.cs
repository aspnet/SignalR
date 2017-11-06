// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Microsoft.AspNetCore.SignalR.Tests.Common;
using Microsoft.AspNetCore.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.AspNetCore.SignalR.Client.FunctionalTests
{
    [Collection(HubConnectionTestsCollection.Name)]
    public class NoNegotiateHubConnectionTests : LoggedTest, IClassFixture<ServerFixture<NoNegotiateStartup>>
    {
        private readonly ServerFixture<NoNegotiateStartup> _serverFixture;

        public NoNegotiateHubConnectionTests(ServerFixture<NoNegotiateStartup> serverFixture, ITestOutputHelper output)
            : base(output)
        {
            if (serverFixture == null)
            {
                throw new ArgumentNullException(nameof(serverFixture));
            }

            _serverFixture = serverFixture;
        }

        [Theory]
        [InlineData(TransportType.LongPolling)]
        [InlineData(TransportType.ServerSentEvents)]
        [InlineData(TransportType.WebSockets)]
        public async Task HubConnectionStartedWithoutProtocolNegotiationWorks(TransportType transportType)
        {
            using (StartLog(out var loggerFactory))
            {
                var connection = new HubConnectionBuilder()
                    .WithUrl(_serverFixture.BaseUrl + "/default")
                    .WithTransport(transportType)
                    .WithLoggerFactory(loggerFactory)
                    .WithHubProtocol(new MessagePackHubProtocol())
                    .WithNoProtocolNegotiation()
                    .Build();

                try
                {
                    await connection.StartAsync().OrTimeout();

                    var result = await connection.InvokeAsync<string>("HelloWorld").OrTimeout();

                    Assert.Equal("Hello World!", result);
                }
                catch (Exception ex)
                {
                    loggerFactory.CreateLogger<NoNegotiateHubConnectionTests>().LogError(ex, "Exception from test");
                    throw;
                }
                finally
                {
                    await connection.DisposeAsync().OrTimeout();
                }
            }
        }
    }
}