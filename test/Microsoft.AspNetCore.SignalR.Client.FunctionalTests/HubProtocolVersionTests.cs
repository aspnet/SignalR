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
using Microsoft.AspNetCore.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.AspNetCore.SignalR.Client.FunctionalTests
{
    public class HubProtocolVersionTestsCollection : ICollectionFixture<InProcessTestServer<VersionStartup>>
    {
        public const string Name = nameof(HubProtocolVersionTestsCollection);
    }

    [Collection(HubProtocolVersionTestsCollection.Name)]
    public class HubProtocolVersionTests : FunctionalTestBase
    {
        public HubProtocolVersionTests(ITestOutputHelper output) : base(output)
        {
        }

        [Theory]
        [MemberData(nameof(TransportTypes))]
        public async Task ClientUsingOldCallWithOriginalProtocol(HttpTransportType transportType)
        {
            using (StartServer<VersionStartup>(out var loggerFactory, out var server, $"{nameof(ClientUsingOldCallWithOriginalProtocol)}_{transportType}"))
            {
                var connectionBuilder = new HubConnectionBuilder()
                    .WithLoggerFactory(loggerFactory)
                    .WithUrl(server.Url + "/version", transportType);

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
            using (StartServer<VersionStartup>(out var loggerFactory, out var server, $"{nameof(ClientUsingOldCallWithNewProtocol)}_{transportType}"))
            {
                var connectionBuilder = new HubConnectionBuilder()
                    .WithLoggerFactory(loggerFactory)
                    .WithUrl(server.Url + "/version", transportType);
                connectionBuilder.Services.AddSingleton<IHubProtocol>(new VersionedJsonHubProtocol(1000));

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
        public async Task ClientUsingNewCallWithNewProtocol(HttpTransportType transportType)
        {
            using (StartServer<VersionStartup>(out var loggerFactory, out var server, $"{nameof(ClientUsingNewCallWithNewProtocol)}_{transportType}"))
            {
                var httpConnectionFactory = new HttpConnectionFactory(Options.Create(new HttpConnectionOptions
                {
                    Url = new Uri(server.Url + "/version"),
                    Transports = transportType
                }), loggerFactory);
                var tcs = new TaskCompletionSource<object>();

                var proxyConnectionFactory = new ProxyConnectionFactory(httpConnectionFactory);

                var connectionBuilder = new HubConnectionBuilder()
                    .WithLoggerFactory(loggerFactory);
                connectionBuilder.Services.AddSingleton<IHubProtocol>(new VersionedJsonHubProtocol(1000));
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
                    var connectionContext = await proxyConnectionFactory.ConnectTask.OrTimeout();

                    // Simulate a new call from the client
                    var messageToken = new JObject
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

        [Theory]
        [MemberData(nameof(TransportTypes))]
        public async Task ClientWithUnsupportedProtocolVersionDoesNotConnect(HttpTransportType transportType)
        {
            bool ExpectedErrors(WriteContext writeContext)
            {
                return writeContext.LoggerName == typeof(HubConnection).FullName;
            }

            using (StartServer<VersionStartup>(out var loggerFactory, out var server, LogLevel.Trace, $"{nameof(ClientWithUnsupportedProtocolVersionDoesNotConnect)}_{transportType}", expectedErrorsFilter: ExpectedErrors))
            {
                var connectionBuilder = new HubConnectionBuilder()
                    .WithLoggerFactory(loggerFactory)
                    .WithUrl(server.Url + "/version", transportType);
                connectionBuilder.Services.AddSingleton<IHubProtocol>(new VersionedJsonHubProtocol(int.MaxValue));

                var connection = connectionBuilder.Build();

                try
                {
                    await ExceptionAssert.ThrowsAsync<HubException>(
                        () => connection.StartAsync(),
                        "Unable to complete handshake with the server due to an error: The server does not support version 2147483647 of the 'json' protocol.").OrTimeout();
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

        private class ProxyConnectionFactory : IConnectionFactory
        {
            private readonly IConnectionFactory _innerFactory;
            public Task<ConnectionContext> ConnectTask { get; private set; }

            public ProxyConnectionFactory(IConnectionFactory innerFactory)
            {
                _innerFactory = innerFactory;
            }

            public Task<ConnectionContext> ConnectAsync(TransferFormat transferFormat, CancellationToken cancellationToken = default)
            {
                ConnectTask = _innerFactory.ConnectAsync(transferFormat, cancellationToken);
                return ConnectTask;
            }

            public Task DisposeAsync(ConnectionContext connection)
            {
                return _innerFactory.DisposeAsync(connection);
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
