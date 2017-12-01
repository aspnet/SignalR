// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Channels;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Microsoft.AspNetCore.SignalR.Tests.Common;
using Microsoft.AspNetCore.Sockets;
using Microsoft.AspNetCore.Sockets.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Xunit;
using Xunit.Abstractions;
using Microsoft.AspNetCore.Sockets.Client.Http;

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
        public async Task CheckFixedMessage(IHubProtocol protocol, TransportType transportType, string path)
        {
            using (StartLog(out var loggerFactory))
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

                    var result = await connection.InvokeAsync<string>("HelloWorld").OrTimeout();

                    Assert.Equal("Hello World!", result);
                }
                catch (Exception ex)
                {
                    loggerFactory.CreateLogger<HubConnectionTests>().LogError(ex, "Exception from test");
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
        public async Task CanSendAndReceiveMessage(IHubProtocol protocol, TransportType transportType, string path)
        {
            using (StartLog(out var loggerFactory))
            {
                const string originalMessage = "SignalR";
                var httpConnection = new HttpConnection(new Uri(_serverFixture.Url + path), transportType, loggerFactory);
                var connection = new HubConnection(httpConnection, protocol, loggerFactory);
                try
                {
                    await connection.StartAsync().OrTimeout();

                    var result = await connection.InvokeAsync<string>("Echo", originalMessage).OrTimeout();

                    Assert.Equal(originalMessage, result);
                }
                catch (Exception ex)
                {
                    loggerFactory.CreateLogger<HubConnectionTests>().LogError(ex, "Exception from test");
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
        public async Task MethodsAreCaseInsensitive(IHubProtocol protocol, TransportType transportType, string path)
        {
            using (StartLog(out var loggerFactory))
            {
                const string originalMessage = "SignalR";
                var uriString = "http://test/" + path;
                var httpConnection = new HttpConnection(new Uri(_serverFixture.Url + path), transportType, loggerFactory);
                var connection = new HubConnection(httpConnection, protocol, loggerFactory);
                try
                {
                    await connection.StartAsync().OrTimeout();

                    var result = await connection.InvokeAsync<string>("echo", originalMessage).OrTimeout();

                    Assert.Equal(originalMessage, result);
                }
                catch (Exception ex)
                {
                    loggerFactory.CreateLogger<HubConnectionTests>().LogError(ex, "Exception from test");
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
        public async Task CanInvokeClientMethodFromServer(IHubProtocol protocol, TransportType transportType, string path)
        {
            using (StartLog(out var loggerFactory))
            {
                const string originalMessage = "SignalR";

                var httpConnection = new HttpConnection(new Uri(_serverFixture.Url + path), transportType, loggerFactory);
                var connection = new HubConnection(httpConnection, protocol, loggerFactory);
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
                    loggerFactory.CreateLogger<HubConnectionTests>().LogError(ex, "Exception from test");
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
        public async Task InvokeNonExistantClientMethodFromServer(IHubProtocol protocol, TransportType transportType, string path)
        {
            using (StartLog(out var loggerFactory))
            {
                var httpConnection = new HttpConnection(new Uri(_serverFixture.Url + path), transportType, loggerFactory);
                var connection = new HubConnection(httpConnection, protocol, loggerFactory);
                try
                {
                    await connection.StartAsync().OrTimeout();
                    await connection.InvokeAsync("CallHandlerThatDoesntExist").OrTimeout();
                    await connection.DisposeAsync().OrTimeout();
                    await connection.Closed.OrTimeout();
                }
                finally
                {
                    await connection.DisposeAsync().OrTimeout();
                }
            }
        }

        [Theory]
        [MemberData(nameof(HubProtocolsAndTransportsAndHubPaths))]
        public async Task CanStreamClientMethodFromServer(IHubProtocol protocol, TransportType transportType, string path)
        {
            using (StartLog(out var loggerFactory))
            {
                var httpConnection = new HttpConnection(new Uri(_serverFixture.Url + path), transportType, loggerFactory);
                var connection = new HubConnection(httpConnection, protocol, loggerFactory);
                try
                {
                    await connection.StartAsync().OrTimeout();

                    var channel = await connection.StreamAsync<int>("Stream", 5).OrTimeout();
                    var results = await channel.ReadAllAsync().OrTimeout();

                    Assert.Equal(new[] { 0, 1, 2, 3, 4 }, results.ToArray());
                }
                catch (Exception ex)
                {
                    loggerFactory.CreateLogger<HubConnectionTests>().LogError(ex, "Exception from test");
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
        public async Task CanCloseStreamMethodEarly(IHubProtocol protocol, TransportType transportType, string path)
        {
            using (StartLog(out var loggerFactory))
            {
                var httpConnection = new HttpConnection(new Uri(_serverFixture.Url + path), transportType, loggerFactory);
                var connection = new HubConnection(httpConnection, protocol, loggerFactory);
                try
                {
                    await connection.StartAsync().OrTimeout();

                    var cts = new CancellationTokenSource();

                    var channel = await connection.StreamAsync<int>("Stream", 1000, cts.Token).OrTimeout();

                    await channel.WaitToReadAsync().OrTimeout();
                    cts.Cancel();

                    var results = await channel.ReadAllAsync().OrTimeout();

                    Assert.True(results.Count > 0 && results.Count < 1000);
                }
                catch (Exception ex)
                {
                    loggerFactory.CreateLogger<HubConnectionTests>().LogError(ex, "Exception from test");
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
        public async Task StreamDoesNotStartIfTokenAlreadyCanceled(IHubProtocol protocol, TransportType transportType, string path)
        {
            using (StartLog(out var loggerFactory))
            {
                var httpConnection = new HttpConnection(new Uri(_serverFixture.Url + path), transportType, loggerFactory);
                var connection = new HubConnection(httpConnection, protocol, loggerFactory);
                try
                {
                    await connection.StartAsync().OrTimeout();

                    var cts = new CancellationTokenSource();
                    cts.Cancel();

                    var channel = await connection.StreamAsync<int>("Stream", 5, cts.Token).OrTimeout();

                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => channel.WaitToReadAsync().OrTimeout());
                }
                catch (Exception ex)
                {
                    loggerFactory.CreateLogger<HubConnectionTests>().LogError(ex, "Exception from test");
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
        public async Task ExceptionFromStreamingSentToClient(IHubProtocol protocol, TransportType transportType, string path)
        {
            using (StartLog(out var loggerFactory))
            {
                var httpConnection = new HttpConnection(new Uri(_serverFixture.Url + path), transportType, loggerFactory);
                var connection = new HubConnection(httpConnection, protocol, loggerFactory);
                try
                {
                    await connection.StartAsync().OrTimeout();
                    var channel = await connection.StreamAsync<int>("StreamException").OrTimeout();

                    var ex = await Assert.ThrowsAsync<HubException>(() => channel.ReadAllAsync().OrTimeout());
                    Assert.Equal("Error occurred while streaming.", ex.Message);
                }
                catch (Exception ex)
                {
                    loggerFactory.CreateLogger<HubConnectionTests>().LogError(ex, "Exception from test");
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
        public async Task ServerThrowsHubExceptionIfHubMethodCannotBeResolved(IHubProtocol hubProtocol, TransportType transportType, string hubPath)
        {
            using (StartLog(out var loggerFactory))
            {
                var httpConnection = new HttpConnection(new Uri(_serverFixture.Url + hubPath), transportType, loggerFactory);
                var connection = new HubConnection(httpConnection, hubProtocol, loggerFactory);
                try
                {
                    await connection.StartAsync().OrTimeout();

                    var ex = await Assert.ThrowsAsync<HubException>(() => connection.InvokeAsync("!@#$%")).OrTimeout();
                    Assert.Equal("Unknown hub method '!@#$%'", ex.Message);
                }
                catch (Exception ex)
                {
                    loggerFactory.CreateLogger<HubConnectionTests>().LogError(ex, "Exception from test");
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
        public async Task ServerThrowsHubExceptionOnHubMethodArgumentCountMismatch(IHubProtocol hubProtocol, TransportType transportType, string hubPath)
        {
            using (StartLog(out var loggerFactory))
            {
                var httpConnection = new HttpConnection(new Uri(_serverFixture.Url + hubPath), transportType, loggerFactory);
                var connection = new HubConnection(httpConnection, hubProtocol, loggerFactory);
                try
                {
                    await connection.StartAsync().OrTimeout();

                    var ex = await Assert.ThrowsAsync<HubException>(() => connection.InvokeAsync("Echo", "p1", 42)).OrTimeout();
                    Assert.Equal("Invocation provides 2 argument(s) but target expects 1.", ex.Message);
                }
                catch (Exception ex)
                {
                    loggerFactory.CreateLogger<HubConnectionTests>().LogError(ex, "Exception from test");
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
        public async Task ServerThrowsHubExceptionOnHubMethodArgumentTypeMismatch(IHubProtocol hubProtocol, TransportType transportType, string hubPath)
        {
            using (StartLog(out var loggerFactory))
            {
                var httpConnection = new HttpConnection(new Uri(_serverFixture.Url + hubPath), transportType, loggerFactory);
                var connection = new HubConnection(httpConnection, hubProtocol, loggerFactory);
                try
                {
                    await connection.StartAsync().OrTimeout();

                    var ex = await Assert.ThrowsAsync<HubException>(() => connection.InvokeAsync("Echo", new int[] { 42 })).OrTimeout();
                    Assert.StartsWith("Error binding arguments. Make sure that the types of the provided values match the types of the hub method being invoked.", ex.Message);
                }
                catch (Exception ex)
                {
                    loggerFactory.CreateLogger<HubConnectionTests>().LogError(ex, "Exception from test");
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
        public async Task ServerThrowsHubExceptionIfStreamingHubMethodCannotBeResolved(IHubProtocol hubProtocol, TransportType transportType, string hubPath)
        {
            using (StartLog(out var loggerFactory))
            {
                var httpConnection = new HttpConnection(new Uri(_serverFixture.Url + hubPath), transportType, loggerFactory);
                var connection = new HubConnection(httpConnection, hubProtocol, loggerFactory);
                try
                {
                    await connection.StartAsync().OrTimeout();

                    var channel = await connection.StreamAsync<int>("!@#$%");
                    var ex = await Assert.ThrowsAsync<HubException>(() => channel.ReadAllAsync().OrTimeout());
                    Assert.Equal("Unknown hub method '!@#$%'", ex.Message);
                }
                catch (Exception ex)
                {
                    loggerFactory.CreateLogger<HubConnectionTests>().LogError(ex, "Exception from test");
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
        public async Task ServerThrowsHubExceptionOnStreamingHubMethodArgumentCountMismatch(IHubProtocol hubProtocol, TransportType transportType, string hubPath)
        {
            using (StartLog(out var loggerFactory))
            {
                loggerFactory.AddConsole(LogLevel.Trace);
                var httpConnection = new HttpConnection(new Uri(_serverFixture.Url + hubPath), transportType, loggerFactory);
                var connection = new HubConnection(httpConnection, hubProtocol, loggerFactory);
                try
                {
                    await connection.StartAsync().OrTimeout();

                    var channel = await connection.StreamAsync<int>("Stream", 42, 42);
                    var ex = await Assert.ThrowsAsync<HubException>(() => channel.ReadAllAsync().OrTimeout());
                    Assert.Equal("Invocation provides 2 argument(s) but target expects 1.", ex.Message);
                }
                catch (Exception ex)
                {
                    loggerFactory.CreateLogger<HubConnectionTests>().LogError(ex, "Exception from test");
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
        public async Task ServerThrowsHubExceptionOnStreamingHubMethodArgumentTypeMismatch(IHubProtocol hubProtocol, TransportType transportType, string hubPath)
        {
            using (StartLog(out var loggerFactory))
            {
                var httpConnection = new HttpConnection(new Uri(_serverFixture.Url + hubPath), transportType, loggerFactory);
                var connection = new HubConnection(httpConnection, hubProtocol, loggerFactory);
                try
                {
                    await connection.StartAsync().OrTimeout();

                    var channel = await connection.StreamAsync<int>("Stream", "xyz");
                    var ex = await Assert.ThrowsAsync<HubException>(() => channel.ReadAllAsync().OrTimeout());
                    Assert.StartsWith("Error binding arguments. Make sure that the types of the provided values match the types of the hub method being invoked.", ex.Message);
                }
                catch (Exception ex)
                {
                    loggerFactory.CreateLogger<HubConnectionTests>().LogError(ex, "Exception from test");
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
        public async Task ServerThrowsHubExceptionIfNonStreamMethodInvokedWithStreamAsync(IHubProtocol hubProtocol, TransportType transportType, string hubPath)
        {
            using (StartLog(out var loggerFactory))
            {
                var httpConnection = new HttpConnection(new Uri(_serverFixture.Url + hubPath), transportType, loggerFactory);
                var connection = new HubConnection(httpConnection, hubProtocol, loggerFactory);
                try
                {
                    await connection.StartAsync().OrTimeout();
                    var channel = await connection.StreamAsync<int>("HelloWorld").OrTimeout();
                    var ex = await Assert.ThrowsAsync<HubException>(() => channel.ReadAllAsync()).OrTimeout();
                    Assert.Equal("The client attempted to invoke the non-streaming 'HelloWorld' method in a streaming fashion.", ex.Message);
                }
                catch (Exception ex)
                {
                    loggerFactory.CreateLogger<HubConnectionTests>().LogError(ex, "Exception from test");
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
        public async Task ServerThrowsHubExceptionIfStreamMethodInvokedWithInvoke(IHubProtocol hubProtocol, TransportType transportType, string hubPath)
        {
            using (StartLog(out var loggerFactory))
            {
                var httpConnection = new HttpConnection(new Uri(_serverFixture.Url + hubPath), transportType, loggerFactory);
                var connection = new HubConnection(httpConnection, hubProtocol, loggerFactory);
                try
                {
                    await connection.StartAsync().OrTimeout();

                    var ex = await Assert.ThrowsAsync<HubException>(() => connection.InvokeAsync("Stream", 3)).OrTimeout();
                    Assert.Equal("The client attempted to invoke the streaming 'Stream' method in a non-streaming fashion.", ex.Message);
                }
                catch (Exception ex)
                {
                    loggerFactory.CreateLogger<HubConnectionTests>().LogError(ex, "Exception from test");
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
        public async Task ServerThrowsHubExceptionIfBuildingAsyncEnumeratorIsNotPossible(IHubProtocol hubProtocol, TransportType transportType, string hubPath)
        {
            using (StartLog(out var loggerFactory))
            {
                var httpConnection = new HttpConnection(new Uri(_serverFixture.Url + hubPath), transportType, loggerFactory);
                var connection = new HubConnection(httpConnection, hubProtocol, loggerFactory);
                try
                {
                    await connection.StartAsync().OrTimeout();
                    var channel = await connection.StreamAsync<int>("StreamBroken").OrTimeout();
                    var ex = await Assert.ThrowsAsync<HubException>(() => channel.ReadAllAsync()).OrTimeout();
                    Assert.Equal("The value returned by the streaming method 'StreamBroken' is null, does not implement the IObservable<> interface or is not a ReadableChannel<>.", ex.Message);
                }
                catch (Exception ex)
                {
                    loggerFactory.CreateLogger<HubConnectionTests>().LogError(ex, "Exception from test");
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
            using (StartLog(out var loggerFactory))
            {
                var httpResponse = await new HttpClient().GetAsync(_serverFixture.Url + "/generateJwtToken");
                httpResponse.EnsureSuccessStatusCode();
                var token = await httpResponse.Content.ReadAsStringAsync();

                var hubConnection = new HubConnectionBuilder()
                    .WithUrl(_serverFixture.Url + "/authorizedhub")
                    .WithTransport(transportType)
                    .WithLoggerFactory(loggerFactory)
                    .WithJwtBearer(() => token)
                    .Build();
                try
                {
                    await hubConnection.StartAsync().OrTimeout();
                    var message = await hubConnection.InvokeAsync<string>("Echo", "Hello, World!").OrTimeout();
                    Assert.Equal("Hello, World!", message);
                }
                catch (Exception ex)
                {
                    loggerFactory.CreateLogger<HubConnectionTests>().LogError(ex, "Exception from test");
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
            using (StartLog(out var loggerFactory))
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
                    var headerValues = await hubConnection.InvokeAsync<string[]>("GetHeaderValues", new object[] { new[] { "X-test", "X-42" } }).OrTimeout();
                    Assert.Equal(new[] { "42", "test" }, headerValues);
                }
                catch (Exception ex)
                {
                    loggerFactory.CreateLogger<HubConnectionTests>().LogError(ex, "Exception from test");
                    throw;
                }
                finally
                {
                    await hubConnection.DisposeAsync().OrTimeout();
                }
            }
        }

        [Fact]
        public async Task WebSocketOptionsAreApplied()
        {
            using (StartLog(out var loggerFactory))
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
                    var cookieValue = await hubConnection.InvokeAsync<string>("GetCookieValue", new object[] { "Foo" }).OrTimeout();
                    Assert.Equal("Bar", cookieValue);
                }
                catch (Exception ex)
                {
                    loggerFactory.CreateLogger<HubConnectionTests>().LogError(ex, "Exception from test");
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
                    foreach (var transport in TransportTypes().SelectMany(t => t))
                    {
                        foreach (var hubPath in HubPaths)
                        {
                            yield return new object[] { protocol, transport, hubPath };
                        }
                    }
                }
            }
        }

        public static string[] HubPaths = new[] { "/default", "/dynamic", "/hubT" };

        public static IEnumerable<IHubProtocol> HubProtocols =>
            new IHubProtocol[]
            {
                new JsonHubProtocol(),
                new MessagePackHubProtocol(),
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
