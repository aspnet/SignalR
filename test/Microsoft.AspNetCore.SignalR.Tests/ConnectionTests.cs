// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO.Pipelines;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Sockets.Client;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Microsoft.AspNetCore.SignalR.Tests
{
    [CollectionDefinition(Name)]
    public class ConnectionTestsCollection : ICollectionFixture<ServerFixture>
    {
        public const string Name = "ConnectionTests";
    }

    [Collection(ConnectionTestsCollection.Name)]
    public class ConnectionTests
    {
        private readonly ServerFixture _serverFixture;

        public ConnectionTests(ServerFixture serverFixture)
        {
            _serverFixture = serverFixture;
        }

        [Fact]
        public async Task CheckConnection()
        {
            var baseUrl = _serverFixture.BaseUrl;
            var loggerFactory = new LoggerFactory();

            using (var httpClient = new HttpClient())
            {
                var transport = new LongPollingTransport(httpClient, loggerFactory);
                using (var connection = await Connection.ConnectAsync(new Uri(baseUrl + "/echo"), transport, httpClient, loggerFactory))
                {

                    var cts = new CancellationTokenSource();

                    // Ready to start the loops
                    var receive =
                        StartReceiving(connection, cts.Token).ContinueWith(_ => cts.Cancel());
                    var send =
                        StartSending(connection, cts.Token).ContinueWith(_ => cts.Cancel());

                    await Task.WhenAll(receive, send);
                }
            }
        }

        private static async Task StartSending(Connection connection, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await connection.Output.WriteAsync(new Message(
                    ReadableBuffer.Create(Encoding.UTF8.GetBytes("Hello World")).Preserve(),
                    Format.Text));
            }
        }

        private static async Task<string> StartReceiving(Connection connection, CancellationToken cancellationToken)
        {
            await connection.Input.WaitToReadAsync(cancellationToken);
            Message message;
            connection.Input.TryRead(out message) ;
            using (message)
            {
                return Encoding.UTF8.GetString(message.Payload.Buffer.ToArray());
            }
        }
    }
}
