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
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Microsoft.AspNetCore.SignalR.Tests;
using Microsoft.AspNetCore.Testing.xunit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.AspNetCore.SignalR.Client.FunctionalTests
{
    [CollectionDefinition(Name)]
    public class HubConnectionTestsCollection : ICollectionFixture<ServerFixture<Startup>>
    {
        public const string Name = "EndToEndTests";
    }

    [Collection(HubConnectionTestsCollection.Name)]
    public class HubConnectionTests : LoggedTest
    {
        private readonly ServerFixture<Startup> _serverFixture;
        public HubConnectionTests(ServerFixture<Startup> serverFixture, ITestOutputHelper output)
            : base(output)
        {
            if (serverFixture == null)
            {
                throw new ArgumentNullException(nameof(serverFixture));
            }

            _serverFixture = serverFixture;
        }

        [Theory]
        [MemberData(nameof(HubProtocolsAndTransportsAndHubPaths))]
        public async Task CheckFixedMessage(string protocolName, TransportType transportType, string path)
        {
            var protocol = HubProtocols[protocolName];
            using (StartLog(out var loggerFactory, $"{nameof(CheckFixedMessage)}_{protocol.Name}_{transportType}_{path.TrimStart('/')}"))
            {
                var connection = new HubConnectionBuilder()
                    .WithUrl(_serverFixture.Url + path)
                    .WithTransport(transportType)
                    .WithLoggerFactory(loggerFactory)
                    .WithHubProtocol(protocol)
                    .Build();

                try
                {
                    await connection.StartAsync().OrTimeout();

                    var result = await connection.InvokeAsync<string>(nameof(TestHub.HelloWorld)).OrTimeout();

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
        [MemberData(nameof(HubProtocolsAndTransportsAndHubPaths))]
        public async Task CanSendAndReceiveMessage(string protocolName, TransportType transportType, string path)
        {
            var protocol = HubProtocols[protocolName];
            using (StartLog(out var loggerFactory, $"{nameof(CanSendAndReceiveMessage)}_{protocol.Name}_{transportType}_{path.TrimStart('/')}"))
            {
                const string originalMessage = "SignalR";
                var connection = new HubConnection(GetHttpConnectionFactory(loggerFactory, path, transportType), protocol, loggerFactory);
                try
                {
                    await connection.StartAsync().OrTimeout();

                    var result = await connection.InvokeAsync<string>(nameof(TestHub.Echo), originalMessage).OrTimeout();

                    Assert.Equal(originalMessage, result);
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
        [MemberData(nameof(HubProtocolsAndTransportsAndHubPaths))]
        public async Task CanStopAndStartConnection(string protocolName, TransportType transportType, string path)
        {
            var protocol = HubProtocols[protocolName];
            using (StartLog(out var loggerFactory, LogLevel.Trace, $"{nameof(CanStopAndStartConnection)}_{protocol.Name}_{transportType}_{path.TrimStart('/')}"))
            {
                const string originalMessage = "SignalR";
                var connection = new HubConnection(GetHttpConnectionFactory(loggerFactory, path, transportType), protocol, loggerFactory);
                try
                {
                    await connection.StartAsync().OrTimeout();
                    var result = await connection.InvokeAsync<string>(nameof(TestHub.Echo), originalMessage).OrTimeout();
                    Assert.Equal(originalMessage, result);
                    await connection.StopAsync().OrTimeout();
                    await connection.StartAsync().OrTimeout();
                    result = await connection.InvokeAsync<string>(nameof(TestHub.Echo), originalMessage).OrTimeout();
                    Assert.Equal(originalMessage, result);
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
        [MemberData(nameof(HubProtocolsAndTransportsAndHubPaths))]
        public async Task CanStartConnectionFromClosedEvent(string protocolName, TransportType transportType, string path)
        {
            var protocol = HubProtocols[protocolName];
            using (StartLog(out var loggerFactory, LogLevel.Trace, $"{nameof(CanStartConnectionFromClosedEvent)}_{protocol.Name}_{transportType}_{path.TrimStart('/')}"))
            {
                var logger = loggerFactory.CreateLogger<HubConnectionTests>();
                const string originalMessage = "SignalR";

                var connection = new HubConnection(GetHttpConnectionFactory(loggerFactory, "/default", transportType), new JsonHubProtocol(), loggerFactory);
                var restartTcs = new TaskCompletionSource<object>();
                connection.Closed += async e =>
                {
                    logger.LogInformation("Closed event triggered");
                    if (!restartTcs.Task.IsCompleted)
                    {
                        logger.LogInformation("Restarting connection");
                        await connection.StartAsync().OrTimeout();
                        logger.LogInformation("Restarted connection");
                        restartTcs.SetResult(null);
                    }
                };

                try
                {
                    await connection.StartAsync().OrTimeout();
                    var result = await connection.InvokeAsync<string>(nameof(TestHub.Echo), originalMessage).OrTimeout();
                    Assert.Equal(originalMessage, result);

                    logger.LogInformation("Stopping connection");
                    await connection.StopAsync().OrTimeout();

                    logger.LogInformation("Waiting for reconnect");
                    await restartTcs.Task.OrTimeout();
                    logger.LogInformation("Reconnection complete");

                    result = await connection.InvokeAsync<string>(nameof(TestHub.Echo), originalMessage).OrTimeout();
                    Assert.Equal(originalMessage, result);

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

        private Func<IConnection> GetHttpConnectionFactory(ILoggerFactory loggerFactory, string path, TransportType transportType)
        {
            return () => new HttpConnection(new Uri(_serverFixture.Url + path), transportType, loggerFactory);
        }

        [Theory]
        [MemberData(nameof(HubProtocolsAndTransportsAndHubPaths))]
        public async Task MethodsAreCaseInsensitive(string protocolName, TransportType transportType, string path)
        {
            var protocol = HubProtocols[protocolName];
            using (StartLog(out var loggerFactory, $"{nameof(MethodsAreCaseInsensitive)}_{protocol.Name}_{transportType}_{path.TrimStart('/')}"))
            {
                const string originalMessage = "SignalR";
                var uriString = "http://test/" + path;
                var connection = new HubConnection(GetHttpConnectionFactory(loggerFactory, path, transportType), protocol, loggerFactory);
                try
                {
                    await connection.StartAsync().OrTimeout();

                    var result = await connection.InvokeAsync<string>(nameof(TestHub.Echo).ToLowerInvariant(), originalMessage).OrTimeout();

                    Assert.Equal(originalMessage, result);
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
        [MemberData(nameof(HubProtocolsAndTransportsAndHubPaths))]
        public async Task CanInvokeClientMethodFromServer(string protocolName, TransportType transportType, string path)
        {
            var protocol = HubProtocols[protocolName];
            using (StartLog(out var loggerFactory, LogLevel.Trace, $"{nameof(CanInvokeClientMethodFromServer)}_{protocol.Name}_{transportType}_{path.TrimStart('/')}"))
            {
                const string originalMessage = "SignalR";

                var connection = new HubConnection(GetHttpConnectionFactory(loggerFactory, path, transportType), protocol, loggerFactory);
                try
                {
                    await connection.StartAsync().OrTimeout();

                    var tcs = new TaskCompletionSource<string>();
                    connection.On<string>("Echo", tcs.SetResult);

                    await connection.InvokeAsync("CallEcho", originalMessage).OrTimeout();

                    Assert.Equal(originalMessage, await tcs.Task.OrTimeout());
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
        [MemberData(nameof(HubProtocolsAndTransportsAndHubPaths))]
        public async Task InvokeNonExistantClientMethodFromServer(string protocolName, TransportType transportType, string path)
        {
            var protocol = HubProtocols[protocolName];
            using (StartLog(out var loggerFactory, LogLevel.Trace, $"{nameof(InvokeNonExistantClientMethodFromServer)}_{protocol.Name}_{transportType}_{path.TrimStart('/')}"))
            {
                var connection = new HubConnection(GetHttpConnectionFactory(loggerFactory, path, transportType), protocol, loggerFactory);
                var closeTcs = new TaskCompletionSource<object>();
                connection.Closed += e =>
                {
                    if (e != null)
                    {
                        closeTcs.SetException(e);
                    }
                    else
                    {
                        closeTcs.SetResult(null);
                    }
                };

                try
                {
                    await connection.StartAsync().OrTimeout();
                    await connection.InvokeAsync("CallHandlerThatDoesntExist").OrTimeout();
                    await connection.DisposeAsync().OrTimeout();
                    await closeTcs.Task.OrTimeout();
                }
                catch (Exception ex)
                {
                    loggerFactory.CreateLogger<HubConnectionTests>().LogError(ex, "{ExceptionType} during test: {Message}", ex.GetType().Name, ex.Message);
                    throw;
                }
                finally
                {
                    await connection.DisposeAsync().OrTimeout();
                }
            }
        }

        [Theory]
        [MemberData(nameof(HubProtocolsAndTransportsAndHubPaths))]
        public async Task CanStreamClientMethodFromServer(string protocolName, TransportType transportType, string path)
        {
            var protocol = HubProtocols[protocolName];
            using (StartLog(out var loggerFactory, LogLevel.Trace, $"{nameof(CanStreamClientMethodFromServer)}_{protocol.Name}_{transportType}_{path.TrimStart('/')}"))
            {
                var connection = new HubConnection(GetHttpConnectionFactory(loggerFactory, path, transportType), protocol, loggerFactory);
                try
                {
                    await connection.StartAsync().OrTimeout();

                    var channel = await connection.StreamAsChannelAsync<int>("Stream", 5).OrTimeout();
                    var results = await channel.ReadAllAsync().OrTimeout();

                    Assert.Equal(new[] { 0, 1, 2, 3, 4 }, results.ToArray());
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
        [MemberData(nameof(HubProtocolsAndTransportsAndHubPaths))]
        public async Task CanCloseStreamMethodEarly(string protocolName, TransportType transportType, string path)
        {
            var protocol = HubProtocols[protocolName];
            using (StartLog(out var loggerFactory, $"{nameof(CanCloseStreamMethodEarly)}_{protocol.Name}_{transportType}_{path.TrimStart('/')}"))
            {
                var connection = new HubConnection(GetHttpConnectionFactory(loggerFactory, path, transportType), protocol, loggerFactory);
                try
                {
                    await connection.StartAsync().OrTimeout();

                    var cts = new CancellationTokenSource();

                    var channel = await connection.StreamAsChannelAsync<int>("Stream", 1000, cts.Token).OrTimeout();

                    // Wait for the server to start streaming items
                    await channel.WaitToReadAsync().AsTask().OrTimeout();

                    cts.Cancel();

                    var results = await channel.ReadAllAsync(suppressExceptions: true).OrTimeout();

                    Assert.True(results.Count > 0 && results.Count < 1000);

                    // We should have been canceled.
                    await Assert.ThrowsAsync<TaskCanceledException>(() => channel.Completion);
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
        [MemberData(nameof(HubProtocolsAndTransportsAndHubPaths))]
        public async Task StreamDoesNotStartIfTokenAlreadyCanceled(string protocolName, TransportType transportType, string path)
        {
            var protocol = HubProtocols[protocolName];
            using (StartLog(out var loggerFactory, LogLevel.Trace, $"{nameof(StreamDoesNotStartIfTokenAlreadyCanceled)}_{protocol.Name}_{transportType}_{path.TrimStart('/')}"))
            {
                var connection =
                    new HubConnection(
                        GetHttpConnectionFactory(loggerFactory, path, transportType), protocol,
                        loggerFactory);
                try
                {
                    await connection.StartAsync().OrTimeout();

                    var cts = new CancellationTokenSource();
                    cts.Cancel();

                    var channel = await connection.StreamAsChannelAsync<int>("Stream", 5, cts.Token).OrTimeout();

                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                        channel.WaitToReadAsync().AsTask().OrTimeout());
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
        [MemberData(nameof(HubProtocolsAndTransportsAndHubPaths))]
        public async Task ExceptionFromStreamingSentToClient(string protocolName, TransportType transportType, string path)
        {
            var protocol = HubProtocols[protocolName];
            using (StartLog(out var loggerFactory, $"{nameof(ExceptionFromStreamingSentToClient)}_{protocol.Name}_{transportType}_{path.TrimStart('/')}"))
            {
                var connection = new HubConnection(GetHttpConnectionFactory(loggerFactory, path, transportType), protocol, loggerFactory);
                try
                {
                    await connection.StartAsync().OrTimeout();
                    var channel = await connection.StreamAsChannelAsync<int>("StreamException").OrTimeout();

                    var ex = await Assert.ThrowsAsync<HubException>(() => channel.ReadAllAsync().OrTimeout());
                    Assert.Equal("An unexpected error occurred invoking 'StreamException' on the server. InvalidOperationException: Error occurred while streaming.", ex.Message);
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
        [MemberData(nameof(HubProtocolsAndTransportsAndHubPaths))]
        public async Task ServerThrowsHubExceptionIfHubMethodCannotBeResolved(string hubProtocolName, TransportType transportType, string hubPath)
        {
            var hubProtocol = HubProtocols[hubProtocolName];
            using (StartLog(out var loggerFactory, $"{nameof(ServerThrowsHubExceptionIfHubMethodCannotBeResolved)}_{hubProtocol.Name}_{transportType}_{hubPath.TrimStart('/')}"))
            {
                var connection = new HubConnection(GetHttpConnectionFactory(loggerFactory, hubPath, transportType), hubProtocol, loggerFactory);
                try
                {
                    await connection.StartAsync().OrTimeout();

                    var ex = await Assert.ThrowsAsync<HubException>(() => connection.InvokeAsync("!@#$%")).OrTimeout();
                    Assert.Equal("Unknown hub method '!@#$%'", ex.Message);
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
        [MemberData(nameof(HubProtocolsAndTransportsAndHubPaths))]
        public async Task ServerThrowsHubExceptionOnHubMethodArgumentCountMismatch(string hubProtocolName, TransportType transportType, string hubPath)
        {
            var hubProtocol = HubProtocols[hubProtocolName];
            using (StartLog(out var loggerFactory, $"{nameof(ServerThrowsHubExceptionOnHubMethodArgumentCountMismatch)}_{hubProtocol.Name}_{transportType}_{hubPath.TrimStart('/')}"))
            {
                var connection = new HubConnection(GetHttpConnectionFactory(loggerFactory, hubPath, transportType), hubProtocol, loggerFactory);
                try
                {
                    await connection.StartAsync().OrTimeout();

                    var ex = await Assert.ThrowsAsync<HubException>(() => connection.InvokeAsync("Echo", "p1", 42)).OrTimeout();
                    Assert.Equal("Failed to invoke 'Echo' due to an error on the server. InvalidDataException: Invocation provides 2 argument(s) but target expects 1.", ex.Message);
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
        [MemberData(nameof(HubProtocolsAndTransportsAndHubPaths))]
        public async Task ServerThrowsHubExceptionOnHubMethodArgumentTypeMismatch(string hubProtocolName, TransportType transportType, string hubPath)
        {
            var hubProtocol = HubProtocols[hubProtocolName];
            using (StartLog(out var loggerFactory, $"{nameof(ServerThrowsHubExceptionOnHubMethodArgumentTypeMismatch)}_{hubProtocol.Name}_{transportType}_{hubPath.TrimStart('/')}"))
            {
                var connection = new HubConnection(GetHttpConnectionFactory(loggerFactory, hubPath, transportType), hubProtocol, loggerFactory);
                try
                {
                    await connection.StartAsync().OrTimeout();

                    var ex = await Assert.ThrowsAsync<HubException>(() => connection.InvokeAsync("Echo", new int[] { 42 })).OrTimeout();
                    Assert.StartsWith("Failed to invoke 'Echo' due to an error on the server.", ex.Message);
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
        [MemberData(nameof(HubProtocolsAndTransportsAndHubPaths))]
        public async Task ServerThrowsHubExceptionIfStreamingHubMethodCannotBeResolved(string hubProtocolName, TransportType transportType, string hubPath)
        {
            var hubProtocol = HubProtocols[hubProtocolName];
            using (StartLog(out var loggerFactory, $"{nameof(ServerThrowsHubExceptionIfStreamingHubMethodCannotBeResolved)}_{hubProtocol.Name}_{transportType}_{hubPath.TrimStart('/')}"))
            {
                var connection = new HubConnection(GetHttpConnectionFactory(loggerFactory, hubPath, transportType), hubProtocol, loggerFactory);
                try
                {
                    await connection.StartAsync().OrTimeout();

                    var channel = await connection.StreamAsChannelAsync<int>("!@#$%");
                    var ex = await Assert.ThrowsAsync<HubException>(() => channel.ReadAllAsync().OrTimeout());
                    Assert.Equal("Unknown hub method '!@#$%'", ex.Message);
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
        [MemberData(nameof(HubProtocolsAndTransportsAndHubPaths))]
        public async Task ServerThrowsHubExceptionOnStreamingHubMethodArgumentCountMismatch(string hubProtocolName, TransportType transportType, string hubPath)
        {
            var hubProtocol = HubProtocols[hubProtocolName];
            using (StartLog(out var loggerFactory, $"{nameof(ServerThrowsHubExceptionOnStreamingHubMethodArgumentCountMismatch)}_{hubProtocol.Name}_{transportType}_{hubPath.TrimStart('/')}"))
            {
                loggerFactory.AddConsole(LogLevel.Trace);
                var connection = new HubConnection(GetHttpConnectionFactory(loggerFactory, hubPath, transportType), hubProtocol, loggerFactory);
                try
                {
                    await connection.StartAsync().OrTimeout();

                    var channel = await connection.StreamAsChannelAsync<int>("Stream", 42, 42);
                    var ex = await Assert.ThrowsAsync<HubException>(() => channel.ReadAllAsync().OrTimeout());
                    Assert.Equal("Failed to invoke 'Stream' due to an error on the server. InvalidDataException: Invocation provides 2 argument(s) but target expects 1.", ex.Message);
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
        [MemberData(nameof(HubProtocolsAndTransportsAndHubPaths))]
        public async Task ServerThrowsHubExceptionOnStreamingHubMethodArgumentTypeMismatch(string hubProtocolName, TransportType transportType, string hubPath)
        {
            var hubProtocol = HubProtocols[hubProtocolName];
            using (StartLog(out var loggerFactory, $"{nameof(ServerThrowsHubExceptionOnStreamingHubMethodArgumentTypeMismatch)}_{hubProtocol.Name}_{transportType}_{hubPath.TrimStart('/')}"))
            {
                var connection = new HubConnection(GetHttpConnectionFactory(loggerFactory, hubPath, transportType), hubProtocol, loggerFactory);
                try
                {
                    await connection.StartAsync().OrTimeout();

                    var channel = await connection.StreamAsChannelAsync<int>("Stream", "xyz");
                    var ex = await Assert.ThrowsAsync<HubException>(() => channel.ReadAllAsync().OrTimeout());
                    Assert.Equal("Failed to invoke 'Stream' due to an error on the server. InvalidDataException: Error binding arguments. Make sure that the types of the provided values match the types of the hub method being invoked.", ex.Message);
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
        [MemberData(nameof(HubProtocolsAndTransportsAndHubPaths))]
        public async Task ServerThrowsHubExceptionIfNonStreamMethodInvokedWithStreamAsync(string hubProtocolName, TransportType transportType, string hubPath)
        {
            var hubProtocol = HubProtocols[hubProtocolName];
            using (StartLog(out var loggerFactory, $"{nameof(ServerThrowsHubExceptionIfNonStreamMethodInvokedWithStreamAsync)}_{hubProtocol.Name}_{transportType}_{hubPath.TrimStart('/')}"))
            {
                var connection = new HubConnection(GetHttpConnectionFactory(loggerFactory, hubPath, transportType), hubProtocol, loggerFactory);
                try
                {
                    await connection.StartAsync().OrTimeout();
                    var channel = await connection.StreamAsChannelAsync<int>("HelloWorld").OrTimeout();
                    var ex = await Assert.ThrowsAsync<HubException>(() => channel.ReadAllAsync()).OrTimeout();
                    Assert.Equal("The client attempted to invoke the non-streaming 'HelloWorld' method in a streaming fashion.", ex.Message);
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
        [MemberData(nameof(HubProtocolsAndTransportsAndHubPaths))]
        public async Task ServerThrowsHubExceptionIfStreamMethodInvokedWithInvoke(string hubProtocolName, TransportType transportType, string hubPath)
        {
            var hubProtocol = HubProtocols[hubProtocolName];
            using (StartLog(out var loggerFactory, $"{nameof(ServerThrowsHubExceptionIfStreamMethodInvokedWithInvoke)}_{hubProtocol.Name}_{transportType}_{hubPath.TrimStart('/')}"))
            {
                var connection = new HubConnection(GetHttpConnectionFactory(loggerFactory, hubPath, transportType), hubProtocol, loggerFactory);
                try
                {
                    await connection.StartAsync().OrTimeout();

                    var ex = await Assert.ThrowsAsync<HubException>(() => connection.InvokeAsync("Stream", 3)).OrTimeout();
                    Assert.Equal("The client attempted to invoke the streaming 'Stream' method in a non-streaming fashion.", ex.Message);
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
        [MemberData(nameof(HubProtocolsAndTransportsAndHubPaths))]
        public async Task ServerThrowsHubExceptionIfBuildingAsyncEnumeratorIsNotPossible(string hubProtocolName, TransportType transportType, string hubPath)
        {
            var hubProtocol = HubProtocols[hubProtocolName];
            using (StartLog(out var loggerFactory, $"{nameof(ServerThrowsHubExceptionIfBuildingAsyncEnumeratorIsNotPossible)}_{hubProtocol.Name}_{transportType}_{hubPath.TrimStart('/')}"))
            {
                var connection = new HubConnection(GetHttpConnectionFactory(loggerFactory, hubPath, transportType), hubProtocol, loggerFactory);
                try
                {
                    await connection.StartAsync().OrTimeout();
                    var channel = await connection.StreamAsChannelAsync<int>("StreamBroken").OrTimeout();
                    var ex = await Assert.ThrowsAsync<HubException>(() => channel.ReadAllAsync()).OrTimeout();
                    Assert.Equal("The value returned by the streaming method 'StreamBroken' is null, does not implement the IObservable<> interface or is not a ReadableChannel<>.", ex.Message);
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
        public async Task ClientCanUseJwtBearerTokenForAuthentication(TransportType transportType)
        {
            using (StartLog(out var loggerFactory, $"{nameof(ClientCanUseJwtBearerTokenForAuthentication)}_{transportType}"))
            {
                var httpResponse = await new HttpClient().GetAsync(_serverFixture.Url + "/generateJwtToken");
                httpResponse.EnsureSuccessStatusCode();
                var token = await httpResponse.Content.ReadAsStringAsync();

                var hubConnection = new HubConnectionBuilder()
                    .WithUrl(_serverFixture.Url + "/authorizedhub")
                    .WithTransport(transportType)
                    .WithLoggerFactory(loggerFactory)
                    .WithAccessToken(() => token)
                    .Build();
                try
                {
                    await hubConnection.StartAsync().OrTimeout();
                    var message = await hubConnection.InvokeAsync<string>(nameof(TestHub.Echo), "Hello, World!").OrTimeout();
                    Assert.Equal("Hello, World!", message);
                }
                catch (Exception ex)
                {
                    loggerFactory.CreateLogger<HubConnectionTests>().LogError(ex, "{ExceptionType} from test", ex.GetType().FullName);
                    throw;
                }
                finally
                {
                    await hubConnection.DisposeAsync().OrTimeout();
                }
            }
        }

        [Theory]
        [MemberData(nameof(TransportTypes))]
        public async Task ClientCanSendHeaders(TransportType transportType)
        {
            using (StartLog(out var loggerFactory, $"{nameof(ClientCanSendHeaders)}_{transportType}"))
            {
                var hubConnection = new HubConnectionBuilder()
                    .WithUrl(_serverFixture.Url + "/default")
                    .WithTransport(transportType)
                    .WithLoggerFactory(loggerFactory)
                    .WithHeader("X-test", "42")
                    .WithHeader("X-42", "test")
                    .Build();
                try
                {
                    await hubConnection.StartAsync().OrTimeout();
                    var headerValues = await hubConnection.InvokeAsync<string[]>(nameof(TestHub.GetHeaderValues), new object[] { new[] { "X-test", "X-42" } }).OrTimeout();
                    Assert.Equal(new[] { "42", "test" }, headerValues);
                }
                catch (Exception ex)
                {
                    loggerFactory.CreateLogger<HubConnectionTests>().LogError(ex, "{ExceptionType} from test", ex.GetType().FullName);
                    throw;
                }
                finally
                {
                    await hubConnection.DisposeAsync().OrTimeout();
                }
            }
        }

        [ConditionalFact]
        [WebSocketsSupportedCondition]
        public async Task WebSocketOptionsAreApplied()
        {
            using (StartLog(out var loggerFactory, $"{nameof(WebSocketOptionsAreApplied)}"))
            {
                // System.Net has a TransportType type which means we need to fully-qualify this rather than 'use' the namespace
                var cookieJar = new System.Net.CookieContainer();
                cookieJar.Add(new System.Net.Cookie("Foo", "Bar", "/", new Uri(_serverFixture.Url).Host));

                var hubConnection = new HubConnectionBuilder()
                    .WithUrl(_serverFixture.Url + "/default")
                    .WithTransport(TransportType.WebSockets)
                    .WithLoggerFactory(loggerFactory)
                    .WithWebSocketOptions(options => options.Cookies = cookieJar)
                    .Build();
                try
                {
                    await hubConnection.StartAsync().OrTimeout();
                    var cookieValue = await hubConnection.InvokeAsync<string>(nameof(TestHub.GetCookieValue), new object[] { "Foo" }).OrTimeout();
                    Assert.Equal("Bar", cookieValue);
                }
                catch (Exception ex)
                {
                    loggerFactory.CreateLogger<HubConnectionTests>().LogError(ex, "{ExceptionType} from test", ex.GetType().FullName);
                    throw;
                }
                finally
                {
                    await hubConnection.DisposeAsync().OrTimeout();
                }
            }
        }

        [Theory]
        [MemberData(nameof(TransportTypes))]
        public async Task CheckHttpConnectionFeatures(TransportType transportType)
        {
            using (StartLog(out var loggerFactory, $"{nameof(CheckHttpConnectionFeatures)}_{transportType}"))
            {
                var hubConnection = new HubConnectionBuilder()
                    .WithUrl(_serverFixture.Url + "/default")
                    .WithTransport(transportType)
                    .WithLoggerFactory(loggerFactory)
                    .Build();
                try
                {
                    await hubConnection.StartAsync().OrTimeout();

                    var features = await hubConnection.InvokeAsync<object[]>(nameof(TestHub.GetIHttpConnectionFeatureProperties)).OrTimeout();
                    var localPort = (Int64)features[0];
                    var remotePort = (Int64)features[1];
                    var localIP = (string)features[2];
                    var remoteIP = (string)features[3];

                    Assert.True(localPort > 0L);
                    Assert.True(remotePort > 0L);
                    Assert.Equal("127.0.0.1", localIP);
                    Assert.Equal("127.0.0.1", remoteIP);
                }
                catch (Exception ex)
                {
                    loggerFactory.CreateLogger<HubConnectionTests>().LogError(ex, "{ExceptionType} from test", ex.GetType().FullName);
                    throw;
                }
                finally
                {
                    await hubConnection.DisposeAsync().OrTimeout();
                }
            }
        }

        [Fact]
        public async Task NegotiationSkipsServerSentEventsWhenUsingBinaryProtocol()
        {
            using (StartLog(out var loggerFactory))
            {
                var hubConnection = new HubConnectionBuilder()
                    .WithUrl(_serverFixture.Url + "/default-nowebsockets")
                    .WithHubProtocol(new MessagePackHubProtocol())
                    .WithLoggerFactory(loggerFactory)
                    .Build();
                try
                {
                    await hubConnection.StartAsync().OrTimeout();

                    var transport = await hubConnection.InvokeAsync<TransportType>(nameof(TestHub.GetActiveTransportName)).OrTimeout();
                    Assert.Equal(TransportType.LongPolling, transport);
                }
                catch (Exception ex)
                {
                    loggerFactory.CreateLogger<HubConnectionTests>().LogError(ex, "{ExceptionType} from test", ex.GetType().FullName);
                    throw;
                }
                finally
                {
                    await hubConnection.DisposeAsync().OrTimeout();
                }
            }
        }

        public static IEnumerable<object[]> HubProtocolsAndTransportsAndHubPaths
        {
            get
            {
                foreach (var protocol in HubProtocols)
                {
                    foreach (var transport in TransportTypes().SelectMany(t => t).Cast<TransportType>())
                    {
                        foreach (var hubPath in HubPaths)
                        {
                            if (!(protocol.Value is MessagePackHubProtocol) || transport != TransportType.ServerSentEvents)
                            {
                                yield return new object[] { protocol.Key, transport, hubPath };
                            }
                        }
                    }
                }
            }
        }

        // This list excludes "special" hub paths like "default-nowebsockets" which exist for specific tests.
        public static string[] HubPaths = new[] { "/default", "/dynamic", "/hubT" };

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
                yield return new object[] { TransportType.WebSockets };
            }
            yield return new object[] { TransportType.ServerSentEvents };
            yield return new object[] { TransportType.LongPolling };
        }
    }
}
