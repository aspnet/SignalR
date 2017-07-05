// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.SignalR.Tests.Common;
using Microsoft.AspNetCore.Sockets;
using Microsoft.AspNetCore.Sockets.Client;
using Microsoft.AspNetCore.Testing.xunit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.AspNetCore.SignalR.Tests
{
    [CollectionDefinition(Name)]
    public class EndToEndTestsCollection : ICollectionFixture<ServerFixture>
    {
        public const string Name = "EndToEndTests";
    }

    [Collection(EndToEndTestsCollection.Name)]
    public class EndToEndTests : LoggedTest
    {
        private readonly ServerFixture _serverFixture;

        public EndToEndTests(ServerFixture serverFixture, ITestOutputHelper output) : base(output)
        {
            if (serverFixture == null)
            {
                throw new ArgumentNullException(nameof(serverFixture));
            }

            _serverFixture = serverFixture;
        }

        [ConditionalFact]
        [OSSkipCondition(OperatingSystems.Windows, WindowsVersions.Win7, WindowsVersions.Win2008R2, SkipReason = "No WebSockets Client for this platform")]
        public async Task WebSocketsTest()
        {
            using (StartLog(out var loggerFactory))
            {
                var logger = loggerFactory.CreateLogger<EndToEndTests>();

                const string message = "Hello, World!";
                using (var ws = new ClientWebSocket())
                {
                    var socketUrl = _serverFixture.WebSocketsUrl + "/echo";

                    logger.LogInformation("Connecting WebSocket to {socketUrl}", socketUrl);
                    await ws.ConnectAsync(new Uri(socketUrl), CancellationToken.None).OrTimeout();

                    var bytes = Encoding.UTF8.GetBytes(message);
                    logger.LogInformation("Sending {length} byte frame", bytes.Length);
                    await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Binary, true, CancellationToken.None).OrTimeout();

                    logger.LogInformation("Receiving frame");
                    var buffer = new ArraySegment<byte>(new byte[1024]);
                    var result = await ws.ReceiveAsync(buffer, CancellationToken.None).OrTimeout();
                    logger.LogInformation("Received {length} byte frame", result.Count);

                    Assert.Equal(bytes, buffer.Array.AsSpan().Slice(0, result.Count).ToArray());

                    logger.LogInformation("Closing socket");
                    await ws.CloseAsync(WebSocketCloseStatus.Empty, "", CancellationToken.None).OrTimeout();
                    logger.LogInformation("Closed socket");
                }
            }
        }

        [ConditionalTheory]
        [OSSkipCondition(OperatingSystems.Windows, WindowsVersions.Win7, WindowsVersions.Win2008R2, SkipReason = "No WebSockets Client for this platform")]
        [MemberData(nameof(TransportTypes))]
        public async Task ConnectionCanSendAndReceiveMessages(TransportType transportType)
        {
            using (StartLog(out var loggerFactory, testName: $"ConnectionCanSendAndReceiveMessages_{transportType.ToString()}"))
            {
                var logger = loggerFactory.CreateLogger<EndToEndTests>();

                const string message = "Major Key";

                var url = _serverFixture.BaseUrl + "/echo";
                var connection = new HttpConnection(new Uri(url), transportType, loggerFactory);
                try
                {
                    var receiveTcs = new TaskCompletionSource<string>();
                    var closeTcs = new TaskCompletionSource<object>();
                    connection.Received += data =>
                    {
                        logger.LogInformation("Received {length} byte message", data.Length);
                        receiveTcs.TrySetResult(Encoding.UTF8.GetString(data));
                        return Task.CompletedTask;
                    };
                    connection.Closed += e =>
                    {
                        logger.LogInformation("Connection closed");
                        if (e != null)
                        {
                            receiveTcs.TrySetException(e);
                            closeTcs.TrySetException(e);
                        }
                        else
                        {
                            receiveTcs.TrySetResult(null);
                            closeTcs.TrySetResult(null);
                        }
                        return Task.CompletedTask;
                    };

                    logger.LogInformation("Starting connection to {url}", url);
                    await connection.StartAsync().OrTimeout();
                    logger.LogInformation("Started connection to {url}", url);

                    var bytes = Encoding.UTF8.GetBytes(message);
                    logger.LogInformation("Sending {length} byte message", bytes.Length);
                    await connection.SendAsync(bytes).OrTimeout();
                    logger.LogInformation("Sent message", bytes.Length);

                    logger.LogInformation("Receiving message");
                    Assert.Equal(message, await receiveTcs.Task.OrTimeout());
                    logger.LogInformation("Completed receive");

                    await closeTcs.Task.OrTimeout();
                }
                finally
                {
                    logger.LogInformation("Disposing Connection");
                    await connection.DisposeAsync().OrTimeout();
                    logger.LogInformation("Disposed Connection");
                }
            }
        }

        [ConditionalTheory]
        [OSSkipCondition(OperatingSystems.Windows, WindowsVersions.Win7, WindowsVersions.Win2008R2, SkipReason = "No WebSockets Client for this platform")]
        [MemberData(nameof(TransportTypes))]
        public async Task ConnectionCanSendAndReceiveMultipleMessages(TransportType transportType)
        {
            using (StartLog(out var loggerFactory, testName: $"ConnectionCanSendAndReceiveMessages_{transportType.ToString()}"))
            {
                var logger = loggerFactory.CreateLogger<EndToEndTests>();

                const string firstMessage = "Major Key";
                const string secondMessage = "Second Message";
                const string closeMessage= "close";

                var url = _serverFixture.BaseUrl + "/multiple";
                var connection = new HttpConnection(new Uri(url), transportType, loggerFactory);
                try
                {
                    var receiveTcs = new TaskCompletionSource<string>();
                    var closeTcs = new TaskCompletionSource<object>();
                    connection.Received += data =>
                    {
                        logger.LogInformation("Received {length} byte message", data.Length);
                        receiveTcs.TrySetResult(Encoding.UTF8.GetString(data));
                        return Task.CompletedTask;
                    };
                    connection.Closed += exception =>
                    {
                        logger.LogInformation("Connection closed");
                        if (exception != null)
                        {
                            receiveTcs.TrySetException(exception);
                            closeTcs.TrySetException(exception);
                        }
                        else
                        {
                            receiveTcs.TrySetResult(null);
                            closeTcs.TrySetResult(null);
                        }
                        return Task.CompletedTask;
                    };

                    logger.LogInformation("Starting connection to {url}", url);
                    await connection.StartAsync().OrTimeout();
                    logger.LogInformation("Started connection to {url}", url);

                    var bytes = Encoding.UTF8.GetBytes(firstMessage);
                    logger.LogInformation("Sending {length} byte message", bytes.Length);
                    await connection.SendAsync(bytes).OrTimeout();
                    logger.LogInformation("Sent message", bytes.Length);

                    logger.LogInformation("Receiving first message");
                    Assert.Equal(firstMessage, await receiveTcs.Task.OrTimeout());
                    logger.LogInformation("Completed first receive");

                    receiveTcs = new TaskCompletionSource<string>();

                    bytes = Encoding.UTF8.GetBytes(secondMessage);
                    logger.LogInformation("Sending {length} byte message", bytes.Length);
                    await connection.SendAsync(bytes).OrTimeout();
                    logger.LogInformation("Sent second message", bytes.Length);

                    logger.LogInformation("Receiving second message");
                    Assert.Equal(secondMessage, await receiveTcs.Task.OrTimeout());
                    logger.LogInformation("Completed second receive");

                    bytes = Encoding.UTF8.GetBytes(closeMessage);
                    logger.LogInformation("Sending {length} byte message", bytes.Length);
                    await connection.SendAsync(bytes).OrTimeout();
                    logger.LogInformation("Sent Close message", bytes.Length);
                    await closeTcs.Task.OrTimeout();
                }
                finally
                {
                    logger.LogInformation("Disposing Connection");
                    await connection.DisposeAsync().OrTimeout();
                    logger.LogInformation("Disposed Connection");
                }
            }
        }

        [ConditionalTheory]
        [OSSkipCondition(OperatingSystems.Windows, WindowsVersions.Win7, WindowsVersions.Win2008R2, SkipReason = "No WebSockets Client for this platform")]
        [MemberData(nameof(TransportTypes))]
        public async Task ConnectionCanSendAndReceiveMultipleMessagesMultipleConnections(TransportType transportType)
        {
            using (StartLog(out var loggerFactory, testName: $"ConnectionCanSendAndReceiveMessages_{transportType.ToString()}"))
            {
                var logger = loggerFactory.CreateLogger<EndToEndTests>();

                const string firstMessage = "Major Key";
                const string secondMessage = "Second Message";
                const string closeMessage = "close";

                var url = _serverFixture.BaseUrl + "/multiple";
                var firstConnection = new HttpConnection(new Uri(url), transportType, loggerFactory);
                var secondConnection = new HttpConnection(new Uri(url), transportType, loggerFactory);

                try
                {
                    var firstConnectionReceiveTcs = new TaskCompletionSource<string>();
                    var firstConnectionCloseTcs = new TaskCompletionSource<object>();
                    var secondConnectionReceiveTcs = new TaskCompletionSource<string>();
                    var secondConnectionCloseTcs = new TaskCompletionSource<object>();

                    firstConnection.Received += data =>
                    {
                        logger.LogInformation("Received {length} byte message", data.Length);
                        firstConnectionReceiveTcs.TrySetResult(Encoding.UTF8.GetString(data));
                        return Task.CompletedTask;
                    };

                    firstConnection.Closed += exception =>
                    {
                        logger.LogInformation("Fisrt connection closed");
                        if (exception != null)
                        {
                            firstConnectionReceiveTcs.TrySetException(exception);
                            firstConnectionCloseTcs.TrySetException(exception);
                        }
                        else
                        {
                            firstConnectionReceiveTcs.TrySetResult(null);
                            firstConnectionCloseTcs.TrySetResult(null);
                        }
                        return Task.CompletedTask;
                    };

                    secondConnection.Received += data =>
                    {
                        logger.LogInformation("Received {length} byte message", data.Length);
                        secondConnectionReceiveTcs.TrySetResult(Encoding.UTF8.GetString(data));
                        return Task.CompletedTask;
                    };

                    secondConnection.Closed += exception =>
                    {
                        logger.LogInformation("Second connection closed");
                        if (exception != null)
                        {
                            secondConnectionReceiveTcs.TrySetException(exception);
                            secondConnectionCloseTcs.TrySetException(exception);
                        }
                        else
                        {
                            secondConnectionReceiveTcs.TrySetResult(null);
                            secondConnectionCloseTcs.TrySetResult(null);
                        }
                        return Task.CompletedTask;
                    };

                    logger.LogInformation("Starting first connection to {url}", url);
                    await firstConnection.StartAsync().OrTimeout();
                    logger.LogInformation("Started first connection to {url}", url);

                    logger.LogInformation("Starting second connection to {url}", url);
                    await secondConnection.StartAsync().OrTimeout();
                    logger.LogInformation("Started second connection to {url}", url);

                    await Send(firstMessage, firstConnection, logger);

                    await AssertMessage(firstMessage, firstConnectionReceiveTcs, secondConnectionReceiveTcs, logger);

                    firstConnectionReceiveTcs = new TaskCompletionSource<string>();
                    secondConnectionReceiveTcs = new TaskCompletionSource<string>();

                    await Send(secondMessage, secondConnection, logger);

                    await AssertMessage(secondMessage, firstConnectionReceiveTcs, secondConnectionReceiveTcs, logger);

                    await Send(closeMessage, firstConnection, logger);
                    await Send(closeMessage, secondConnection, logger);
                }
                finally
                {
                    logger.LogInformation("Disposing first Connection");
                    await firstConnection.DisposeAsync().OrTimeout();
                    logger.LogInformation("Disposed first Connection");

                    logger.LogInformation("Disposing secondConnection");
                    await secondConnection.DisposeAsync().OrTimeout();
                    logger.LogInformation("Disposed second Connection");
                }
            }
        }

        public static IEnumerable<object[]> MessageSizesData
        {
            get
            {
                yield return new object[] { new string('A', 5 * 4096) };
                yield return new object[] { new string('A', 1000 * 4096 + 32) };
            }
        }

        [ConditionalTheory]
        [OSSkipCondition(OperatingSystems.Windows, WindowsVersions.Win7, WindowsVersions.Win2008R2, SkipReason = "No WebSockets Client for this platform")]
        [MemberData(nameof(MessageSizesData))]
        public async Task ConnectionCanSendAndReceiveDifferentMessageSizesWebSocketsTransport(string message)
        {
            using (StartLog(out var loggerFactory, testName: $"ConnectionCanSendAndReceiveDifferentMessageSizesWebSocketsTransport_{message.Length}"))
            {
                var logger = loggerFactory.CreateLogger<EndToEndTests>();

                var url = _serverFixture.BaseUrl + "/echo";
                var connection = new HttpConnection(new Uri(url), loggerFactory);
                try
                {
                    var receiveTcs = new TaskCompletionSource<byte[]>();
                    connection.Received += data =>
                    {
                        logger.LogInformation("Received {length} byte message", data.Length);
                        receiveTcs.TrySetResult(data);
                        return Task.CompletedTask;
                    };

                    logger.LogInformation("Starting connection to {url}", url);
                    await connection.StartAsync().OrTimeout();
                    logger.LogInformation("Started connection to {url}", url);

                    var bytes = Encoding.UTF8.GetBytes(message);
                    logger.LogInformation("Sending {length} byte message", bytes.Length);
                    await connection.SendAsync(bytes).OrTimeout();
                    logger.LogInformation("Sent message", bytes.Length);

                    logger.LogInformation("Receiving message");
                    var receivedData = await receiveTcs.Task.OrTimeout();
                    Assert.Equal(message, Encoding.UTF8.GetString(receivedData));
                    logger.LogInformation("Completed receive");
                }
                finally
                {
                    logger.LogInformation("Disposing Connection");
                    await connection.DisposeAsync().OrTimeout();
                    logger.LogInformation("Disposed Connection");
                }
            }
        }


        [ConditionalFact]
        [OSSkipCondition(OperatingSystems.Windows, WindowsVersions.Win7, WindowsVersions.Win2008R2, SkipReason = "No WebSockets Client for this platform")]
        public async Task ServerClosesConnectionWithErrorIfHubCannotBeCreated_WebSocket()
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await ServerClosesConnectionWithErrorIfHubCannotBeCreated(TransportType.WebSockets));
            Assert.Equal("Websocket closed with error: InternalServerError.", exception.Message);
        }

        [Fact]
        public async Task ServerClosesConnectionWithErrorIfHubCannotBeCreated_LongPolling()
        {
            var exception = await Assert.ThrowsAsync<HttpRequestException>(
                async () => await ServerClosesConnectionWithErrorIfHubCannotBeCreated(TransportType.LongPolling));
            Assert.Equal("Response status code does not indicate success: 500 (Internal Server Error).", exception.Message);
        }

        private async Task ServerClosesConnectionWithErrorIfHubCannotBeCreated(TransportType transportType)
        {
            using (StartLog(out var loggerFactory, testName: $"ConnectionCanSendAndReceiveMessages_{transportType.ToString()}"))
            {
                var logger = loggerFactory.CreateLogger<EndToEndTests>();

                var url = _serverFixture.BaseUrl + "/uncreatable";

                var connection = new HubConnection(new HttpConnection(new Uri(url), transportType, loggerFactory), loggerFactory);
                try
                {
                    var closeTcs = new TaskCompletionSource<object>();

                    connection.Closed += e =>
                    {
                        logger.LogInformation("Connection closed");
                        if (e != null)
                        {
                            closeTcs.TrySetException(e);
                        }
                        else
                        {
                            closeTcs.TrySetResult(null);
                        }
                        return Task.CompletedTask;
                    };

                    logger.LogInformation("Starting connection to {url}", url);
                    await connection.StartAsync().OrTimeout();

                    await closeTcs.Task.OrTimeout();
                }
                finally
                {
                    logger.LogInformation("Disposing Connection");
                    await connection.DisposeAsync().OrTimeout();
                    logger.LogInformation("Disposed Connection");
                }
            }
        }

        public static IEnumerable<object[]> TransportTypes() =>
            new[]
            {
                new object[] { TransportType.WebSockets },
                new object[] { TransportType.ServerSentEvents },
                new object[] { TransportType.LongPolling }
            };

        private async Task AssertMessage(string expectedMessage, TaskCompletionSource<string> firstConnectionReceiveTcs, TaskCompletionSource<string> secondConnectionReceiveTcs, ILogger logger)
        {
            logger.LogInformation("Receiving message on first connection");
            Assert.Equal(expectedMessage, await firstConnectionReceiveTcs.Task.OrTimeout());
            logger.LogInformation("Completed receive on first connection");

            logger.LogInformation("Receiving message on second connection");
            Assert.Equal(expectedMessage, await secondConnectionReceiveTcs.Task.OrTimeout());
            logger.LogInformation("Completed receive on second connection");
        }

        private async Task Send(string message, HttpConnection connection, ILogger logger)
        {
            var bytes = Encoding.UTF8.GetBytes(message);
            logger.LogInformation("Sending {length} byte message", bytes.Length);
            await connection.SendAsync(bytes).OrTimeout();
            logger.LogInformation("Sent message", bytes.Length); ;
        }
    }
}
