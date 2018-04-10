// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Net.Http;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Http.Connections.Client;
using Microsoft.AspNetCore.Http.Connections.Client.Internal;
using Microsoft.AspNetCore.Testing.xunit;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Microsoft.AspNetCore.SignalR.Tests
{
    public class DefaultTransportFactoryTests
    {
        private const HttpTransportTypes AllTransportTypes = HttpTransportTypes.WebSockets | HttpTransportTypes.ServerSentEvents | HttpTransportTypes.LongPolling;

        [Theory]
        [InlineData(HttpTransportTypes.None)]
        [InlineData((HttpTransportTypes)int.MaxValue)]
        public void DefaultTransportFactoryCanBeCreatedWithNoOrUnknownTransportTypeFlags(HttpTransportTypes transportType)
        {
            Assert.NotNull(new DefaultTransportFactory(transportType, new LoggerFactory(), new HttpClient(), httpOptions: null));
        }

        [Theory]
        [InlineData(AllTransportTypes)]
        [InlineData(HttpTransportTypes.LongPolling)]
        [InlineData(HttpTransportTypes.ServerSentEvents)]
        [InlineData(HttpTransportTypes.LongPolling | HttpTransportTypes.WebSockets)]
        [InlineData(HttpTransportTypes.ServerSentEvents | HttpTransportTypes.WebSockets)]
        public void DefaultTransportFactoryCannotBeCreatedWithoutHttpClient(HttpTransportTypes transportType)
        {
            var exception = Assert.Throws<ArgumentNullException>(
                () => new DefaultTransportFactory(transportType, new LoggerFactory(), httpClient: null, httpOptions: null));

            Assert.Equal("httpClient", exception.ParamName);
        }

        [Fact]
        public void DefaultTransportFactoryCanBeCreatedWithoutHttpClientIfWebSocketsTransportRequestedExplicitly()
        {
            new DefaultTransportFactory(HttpTransportTypes.WebSockets, new LoggerFactory(), httpClient: null, httpOptions: null);
        }

        [ConditionalTheory]
        [InlineData(HttpTransportTypes.WebSockets, typeof(WebSocketsTransport))]
        [InlineData(HttpTransportTypes.ServerSentEvents, typeof(ServerSentEventsTransport))]
        [InlineData(HttpTransportTypes.LongPolling, typeof(LongPollingTransport))]
        [WebSocketsSupportedCondition]
        public void DefaultTransportFactoryCreatesRequestedTransportIfAvailable(HttpTransportTypes requestedTransport, Type expectedTransportType)
        {
            var transportFactory = new DefaultTransportFactory(requestedTransport, loggerFactory: null, httpClient: new HttpClient(), httpOptions: null);
            Assert.IsType(expectedTransportType,
                transportFactory.CreateTransport(AllTransportTypes));
        }

        [Theory]
        [InlineData(HttpTransportTypes.WebSockets)]
        [InlineData(HttpTransportTypes.ServerSentEvents)]
        [InlineData(HttpTransportTypes.LongPolling)]
        [InlineData(AllTransportTypes)]
        public void DefaultTransportFactoryThrowsIfItCannotCreateRequestedTransport(HttpTransportTypes requestedTransport)
        {
            var transportFactory =
                new DefaultTransportFactory(requestedTransport, loggerFactory: null, httpClient: new HttpClient(), httpOptions: null);
            var ex = Assert.Throws<InvalidOperationException>(
                () => transportFactory.CreateTransport(~requestedTransport));

            Assert.Equal("No requested transports available on the server.", ex.Message);
        }

        [ConditionalFact]
        [WebSocketsSupportedCondition]
        public void DefaultTransportFactoryCreatesWebSocketsTransportIfAvailable()
        {
            Assert.IsType<WebSocketsTransport>(
                new DefaultTransportFactory(AllTransportTypes, loggerFactory: null, httpClient: new HttpClient(), httpOptions: null)
                    .CreateTransport(AllTransportTypes));
        }

        [Theory]
        [InlineData(AllTransportTypes, typeof(ServerSentEventsTransport))]
        [InlineData(HttpTransportTypes.ServerSentEvents, typeof(ServerSentEventsTransport))]
        [InlineData(HttpTransportTypes.LongPolling, typeof(LongPollingTransport))]
        public void DefaultTransportFactoryCreatesRequestedTransportIfAvailable_Win7(HttpTransportTypes requestedTransport, Type expectedTransportType)
        {
            if (!TestHelpers.IsWebSocketsSupported())
            {
                var transportFactory = new DefaultTransportFactory(requestedTransport, loggerFactory: null, httpClient: new HttpClient(), httpOptions: null);
                Assert.IsType(expectedTransportType,
                    transportFactory.CreateTransport(AllTransportTypes));
            }
        }

        [Theory]
        [InlineData(HttpTransportTypes.WebSockets)]
        public void DefaultTransportFactoryThrowsIfItCannotCreateRequestedTransport_Win7(HttpTransportTypes requestedTransport)
        {
            if (!TestHelpers.IsWebSocketsSupported())
            {
                var transportFactory =
                    new DefaultTransportFactory(requestedTransport, loggerFactory: null, httpClient: new HttpClient(), httpOptions: null);
                var ex = Assert.Throws<InvalidOperationException>(
                    () => transportFactory.CreateTransport(AllTransportTypes));

                Assert.Equal("No requested transports available on the server.", ex.Message);
            }
        }
    }
}
