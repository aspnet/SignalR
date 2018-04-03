// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Net;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using TransportType = Microsoft.AspNetCore.Http.Connections.TransportType;

namespace Microsoft.AspNetCore.SignalR.Client.Tests
{
    public class HubConnectionBuilderExtensionsTests
    {
        [Fact]
        public void WithHttpConnectionSetsUrl()
        {
            var connectionBuilder = new HubConnectionBuilder();
            connectionBuilder.WithHttpConnection("http://tempuri.org");

            var serviceProvider = connectionBuilder.Services.BuildServiceProvider();

            var value = serviceProvider.GetService<IOptions<HttpConnectionOptions>>().Value;

            Assert.Equal(new Uri("http://tempuri.org"), value.Url);
        }

        [Fact]
        public void WithHttpConnectionSetsTransport()
        {
            var connectionBuilder = new HubConnectionBuilder();
            connectionBuilder.WithHttpConnection("http://tempuri.org", TransportType.LongPolling);

            var serviceProvider = connectionBuilder.Services.BuildServiceProvider();

            var value = serviceProvider.GetService<IOptions<HttpConnectionOptions>>().Value;

            Assert.Equal(TransportType.LongPolling, value.Transport);
        }

        [Fact]
        public void WithHttpConnectionCallsConfigure()
        {
            var proxy = Mock.Of<IWebProxy>();

            var connectionBuilder = new HubConnectionBuilder();
            connectionBuilder.WithHttpConnection("http://tempuri.org", options => { options.Proxy = proxy; });

            var serviceProvider = connectionBuilder.Services.BuildServiceProvider();

            var value = serviceProvider.GetService<IOptions<HttpConnectionOptions>>().Value;

            Assert.Same(proxy, value.Proxy);
        }

        [Fact]
        public void WithConsoleLoggerAddsLogger()
        {
            var loggingFactory = Mock.Of<ILoggerFactory>();

            var connectionBuilder = new HubConnectionBuilder();
            connectionBuilder.WithLoggerFactory(loggingFactory);

            var serviceProvider = connectionBuilder.Services.BuildServiceProvider();

            var resolvedLoggingFactory = serviceProvider.GetService<ILoggerFactory>();

            Assert.Same(resolvedLoggingFactory, loggingFactory);
        }

        [Fact]
        public void WithHubProtocolAddsProtocol()
        {
            var hubProtocol = Mock.Of<IHubProtocol>();

            var connectionBuilder = new HubConnectionBuilder();
            connectionBuilder.WithHubProtocol(hubProtocol);

            var serviceProvider = connectionBuilder.Services.BuildServiceProvider();

            var resolvedHubProtocol = serviceProvider.GetService<IHubProtocol>();

            Assert.Same(hubProtocol, resolvedHubProtocol);
        }

        [Fact]
        public void WithJsonProtocolAddsProtocol()
        {
            var connectionBuilder = new HubConnectionBuilder();
            connectionBuilder.WithJsonProtocol();

            var serviceProvider = connectionBuilder.Services.BuildServiceProvider();

            var resolvedHubProtocol = serviceProvider.GetService<IHubProtocol>();

            Assert.IsType<JsonHubProtocol>(resolvedHubProtocol);
        }

        [Fact]
        public void WithMessagePackProtocolAddsProtocol()
        {
            var connectionBuilder = new HubConnectionBuilder();
            connectionBuilder.WithMessagePackProtocol();

            var serviceProvider = connectionBuilder.Services.BuildServiceProvider();

            var resolvedHubProtocol = serviceProvider.GetService<IHubProtocol>();

            Assert.IsType<MessagePackHubProtocol>(resolvedHubProtocol);
        }
    }
}