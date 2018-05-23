// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Http.Connections.Client;
using Microsoft.AspNetCore.SignalR.Protocol;
using Microsoft.AspNetCore.SignalR.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
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
        public async Task ClientUsingOldCallWithOriginalProtocol(HttpTransportType transportType)
        {
            using (StartVerifiableLog(out var loggerFactory, $"{nameof(ClientUsingOldCallWithOriginalProtocol)}_{transportType}"))
            {
                var connectionBuilder = new HubConnectionBuilder()
                    .WithLoggerFactory(loggerFactory)
                    .WithUrl(ServerFixture.Url + "/version", transportType);

                var connection = connectionBuilder.Build();

                try
                {
                    await connection.StartAsync().OrTimeout();

                    var result = await connection.InvokeAsync<string>(nameof(VersionHub.Echo), "Hello World!").OrTimeout();

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

        [Theory]
        [MemberData(nameof(TransportTypes))]
        public async Task ClientUsingOldCallWithNewProtocol(HttpTransportType transportType)
        {
            using (StartVerifiableLog(out var loggerFactory, $"{nameof(ClientUsingOldCallWithNewProtocol)}_{transportType}"))
            {
                var connectionBuilder = new HubConnectionBuilder()
                    .WithLoggerFactory(loggerFactory)
                    .WithUrl(ServerFixture.Url + "/version", transportType);
                connectionBuilder.Services.AddSingleton<IHubProtocol>(new VersionedJsonHubProtocol());

                var connection = connectionBuilder.Build();

                try
                {
                    await connection.StartAsync().OrTimeout();

                    var result = await connection.InvokeAsync<string>(nameof(VersionHub.Echo), "Hello World!").OrTimeout();

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

        public class ProxyConnectionFactory : IConnectionFactory
        {
            private readonly IConnectionFactory _innerFactory;
            internal Task<ConnectionContext> _connectTask;

            public ProxyConnectionFactory(IConnectionFactory innerFactory)
            {
                _innerFactory = innerFactory;
            }

            public Task<ConnectionContext> ConnectAsync(TransferFormat transferFormat, CancellationToken cancellationToken = default)
            {
                _connectTask = _innerFactory.ConnectAsync(transferFormat, cancellationToken);
                return _connectTask;
            }

            public Task DisposeAsync(ConnectionContext connection)
            {
                return _innerFactory.DisposeAsync(connection);
            }
        }

        [Theory]
        [MemberData(nameof(TransportTypes))]
        public async Task ClientUsingNewCallWithNewProtocol(HttpTransportType transportType)
        {
            using (StartVerifiableLog(out var loggerFactory, $"{nameof(ClientUsingNewCallWithNewProtocol)}_{transportType}"))
            {
                var httpConnectionFactory = new HttpConnectionFactory(Options.Create(new HttpConnectionOptions
                {
                    Url = new Uri(ServerFixture.Url + "/version"),
                    Transports = transportType
                }), loggerFactory);
                var tcs = new TaskCompletionSource<object>();

                ProxyConnectionFactory proxyConnectionFactory = new ProxyConnectionFactory(httpConnectionFactory);

                var connectionBuilder = new HubConnectionBuilder()
                    .WithLoggerFactory(loggerFactory);
                connectionBuilder.Services.AddSingleton<IHubProtocol>(new VersionedJsonHubProtocol());
                connectionBuilder.Services.AddSingleton<IConnectionFactory>(proxyConnectionFactory);

                var connection = connectionBuilder.Build();
                connection.On("NewProtocolMethodClient", () =>
                {
                    tcs.SetResult(null);
                });

                try
                {
                    await connection.StartAsync().OrTimeout();

                    // Task should already have been awaited in StartAsync
                    var connectionContext = await proxyConnectionFactory._connectTask;

                    JObject messageToken = new JObject
                    {
                        ["type"] = int.MaxValue
                    };

                    connectionContext.Transport.Output.Write(Encoding.UTF8.GetBytes(messageToken.ToString()));
                    connectionContext.Transport.Output.Write(new[] { (byte)0x1e });
                    await connectionContext.Transport.Output.FlushAsync().OrTimeout();

                    await tcs.Task.OrTimeout();
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
