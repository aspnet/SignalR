// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Sockets;
using Microsoft.AspNetCore.Sockets.Client;
using Microsoft.AspNetCore.Sockets.Features;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Xunit;
using Xunit.Abstractions;

// This is needed because there's a System.Net.TransportType in net461 (it's internal in netcoreapp).
using TransportType = Microsoft.AspNetCore.Sockets.TransportType;

namespace Microsoft.AspNetCore.SignalR.Client.Tests
{
    public partial class HttpConnectionTests : LoggedTest
    {
        public HttpConnectionTests(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public void CannotCreateConnectionWithNullUrl()
        {
            var exception = Assert.Throws<ArgumentNullException>(() => new HttpConnection(null));
            Assert.Equal("url", exception.ParamName);
        }

        [Fact]
        public void ConnectionReturnsUrlUsedToStartTheConnection()
        {
            var connectionUrl = new Uri("http://fakeuri.org/");
            Assert.Equal(connectionUrl, new HttpConnection(connectionUrl).Url);
        }

        [Theory]
        [InlineData((TransportType)0)]
        [InlineData(TransportType.All + 1)]
        public void CannotStartConnectionWithInvalidTransportType(TransportType requestedTransportType)
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new HttpConnection(new Uri("http://fakeuri.org/"), requestedTransportType));
        }

        [Fact]
        public async Task StartAsyncSetsTransferModeFeature()
        {
            var testTransport = new TestTransport(transferMode: TransferMode.Binary);
            await WithConnectionAsync(
                CreateConnection(transport: testTransport),
                async (connection, closed) =>
                {
                    Assert.Null(connection.Features.Get<ITransferModeFeature>());
                    await connection.StartAsync().OrTimeout();

                    var transferModeFeature = connection.Features.Get<ITransferModeFeature>();
                    Assert.NotNull(transferModeFeature);
                    Assert.Equal(TransferMode.Binary, transferModeFeature.TransferMode);
                });
        }
    }
}
