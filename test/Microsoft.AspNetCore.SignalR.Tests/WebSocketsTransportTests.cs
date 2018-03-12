// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Sockets;
using Microsoft.AspNetCore.Sockets.Client;
using Microsoft.AspNetCore.Sockets.Client.Http;
using Microsoft.AspNetCore.Testing.xunit;
using Microsoft.Extensions.Logging.Testing;
using Moq;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.AspNetCore.SignalR.Tests
{
    [Collection(EndToEndTestsCollection.Name)]
    public class WebSocketsTransportTests : LoggedTest
    {
        private readonly ServerFixture<Startup> _serverFixture;

        public WebSocketsTransportTests(ServerFixture<Startup> serverFixture, ITestOutputHelper output) : base(output)
        {
            if (serverFixture == null)
            {
                throw new ArgumentNullException(nameof(serverFixture));
            }

            _serverFixture = serverFixture;
        }

        [ConditionalFact]
        [OSSkipCondition(OperatingSystems.Windows, WindowsVersions.Win7, WindowsVersions.Win2008R2, SkipReason = "No WebSockets Client for this platform")]
        public void HttpOptionsSetOntoWebSocketOptions()
        {
            ClientWebSocketOptions webSocketsOptions = null;

            HttpOptions httpOptions = new HttpOptions();
            httpOptions.Cookies.Add(new Cookie("Name", "Value", string.Empty, "fakeuri.org"));
            var clientCertificate = new X509Certificate();
            httpOptions.ClientCertificates.Add(clientCertificate);
            httpOptions.UseDefaultCredentials = false;
            httpOptions.Credentials = Mock.Of<ICredentials>();
            httpOptions.Proxy = Mock.Of<IWebProxy>();
            httpOptions.WebSocketOptions = options => webSocketsOptions = options;

            var webSocketsTransport = new WebSocketsTransport(httpOptions: httpOptions, loggerFactory: null);
            Assert.NotNull(webSocketsTransport);

            Assert.NotNull(webSocketsOptions);
            Assert.Equal(1, webSocketsOptions.Cookies.Count);
            Assert.Single(webSocketsOptions.ClientCertificates);
            Assert.Same(clientCertificate, webSocketsOptions.ClientCertificates[0]);
            Assert.False(webSocketsOptions.UseDefaultCredentials);
            Assert.Same(httpOptions.Proxy, webSocketsOptions.Proxy);
            Assert.Same(httpOptions.Credentials, webSocketsOptions.Credentials);
        }

        [ConditionalFact]
        [OSSkipCondition(OperatingSystems.Windows, WindowsVersions.Win7, WindowsVersions.Win2008R2, SkipReason = "No WebSockets Client for this platform")]
        public async Task WebSocketsTransportStopsSendAndReceiveLoopsWhenTransportIsStopped()
        {
            using (StartLog(out var loggerFactory))
            {
                var pair = DuplexPipe.CreateConnectionPair(PipeOptions.Default, PipeOptions.Default);
                var webSocketsTransport = new WebSocketsTransport(httpOptions: null, loggerFactory: loggerFactory);
                await webSocketsTransport.StartAsync(new Uri(_serverFixture.WebSocketsUrl + "/echo"), pair.Application,
                    TransferFormat.Binary, connection: Mock.Of<IConnection>()).OrTimeout();
                await webSocketsTransport.StopAsync().OrTimeout();
                await webSocketsTransport.Running.OrTimeout();
            }
        }

        [ConditionalFact(Skip = "Issue in ClientWebSocket prevents user-agent being set - https://github.com/dotnet/corefx/issues/26627")]
        [OSSkipCondition(OperatingSystems.Windows, WindowsVersions.Win7, WindowsVersions.Win2008R2, SkipReason = "No WebSockets Client for this platform")]
        public async Task WebSocketsTransportSendsUserAgent()
        {
            using (StartLog(out var loggerFactory))
            {
                var pair = DuplexPipe.CreateConnectionPair(PipeOptions.Default, PipeOptions.Default);
                var webSocketsTransport = new WebSocketsTransport(httpOptions: null, loggerFactory: loggerFactory);
                await webSocketsTransport.StartAsync(new Uri(_serverFixture.WebSocketsUrl + "/httpheader"), pair.Application,
                    TransferFormat.Binary, connection: Mock.Of<IConnection>()).OrTimeout();

                await pair.Transport.Output.WriteAsync(Encoding.UTF8.GetBytes("User-Agent"));

                // The HTTP header endpoint closes the connection immediately after sending response which should stop the transport
                await webSocketsTransport.Running.OrTimeout();

                Assert.True(pair.Transport.Input.TryRead(out var result));

                string userAgent = Encoding.UTF8.GetString(result.Buffer.ToArray());

                // user agent version should come from version embedded in assembly metadata
                var assemblyVersion = typeof(Constants)
                    .Assembly
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>();

                Assert.Equal("Microsoft.AspNetCore.Sockets.Client.Http/" + assemblyVersion.InformationalVersion, userAgent);
            }
        }

        [ConditionalFact]
        [OSSkipCondition(OperatingSystems.Windows, WindowsVersions.Win7, WindowsVersions.Win2008R2, SkipReason = "No WebSockets Client for this platform")]
        public async Task WebSocketsTransportStopsWhenConnectionChannelClosed()
        {
            using (StartLog(out var loggerFactory))
            {
                var pair = DuplexPipe.CreateConnectionPair(PipeOptions.Default, PipeOptions.Default);
                var webSocketsTransport = new WebSocketsTransport(httpOptions: null, loggerFactory: loggerFactory);
                await webSocketsTransport.StartAsync(new Uri(_serverFixture.WebSocketsUrl + "/echo"), pair.Application,
                    TransferFormat.Binary, connection: Mock.Of<IConnection>());
                pair.Transport.Output.Complete();
                await webSocketsTransport.Running.OrTimeout(TimeSpan.FromSeconds(10));
            }
        }

        [ConditionalTheory]
        [OSSkipCondition(OperatingSystems.Windows, WindowsVersions.Win7, WindowsVersions.Win2008R2, SkipReason = "No WebSockets Client for this platform")]
        [InlineData(TransferFormat.Text)]
        [InlineData(TransferFormat.Binary)]
        public async Task WebSocketsTransportStopsWhenConnectionClosedByTheServer(TransferFormat transferFormat)
        {
            using (StartLog(out var loggerFactory))
            {
                var pair = DuplexPipe.CreateConnectionPair(PipeOptions.Default, PipeOptions.Default);
                var webSocketsTransport = new WebSocketsTransport(httpOptions: null, loggerFactory: loggerFactory);
                await webSocketsTransport.StartAsync(new Uri(_serverFixture.WebSocketsUrl + "/echo"), pair.Application, transferFormat, connection: Mock.Of<IConnection>());

                await pair.Transport.Output.WriteAsync(new byte[] { 0x42 });

                // The echo endpoint closes the connection immediately after sending response which should stop the transport
                await webSocketsTransport.Running.OrTimeout();

                Assert.True(pair.Transport.Input.TryRead(out var result));
                Assert.Equal(new byte[] { 0x42 }, result.Buffer.ToArray());
                pair.Transport.Input.AdvanceTo(result.Buffer.End);
            }
        }

        [ConditionalTheory]
        [OSSkipCondition(OperatingSystems.Windows, WindowsVersions.Win7, WindowsVersions.Win2008R2, SkipReason = "No WebSockets Client for this platform")]
        [InlineData(TransferFormat.Text)]
        [InlineData(TransferFormat.Binary)]
        public async Task WebSocketsTransportSetsTransferFormat(TransferFormat transferFormat)
        {
            using (StartLog(out var loggerFactory))
            {
                var pair = DuplexPipe.CreateConnectionPair(PipeOptions.Default, PipeOptions.Default);
                var webSocketsTransport = new WebSocketsTransport(httpOptions: null, loggerFactory: loggerFactory);

                await webSocketsTransport.StartAsync(new Uri(_serverFixture.WebSocketsUrl + "/echo"), pair.Application,
                    transferFormat, connection: Mock.Of<IConnection>()).OrTimeout();

                await webSocketsTransport.StopAsync().OrTimeout();
                await webSocketsTransport.Running.OrTimeout();
            }
        }

        [ConditionalTheory]
        [InlineData(TransferFormat.Text | TransferFormat.Binary)] // Multiple values not allowed
        [InlineData((TransferFormat)42)] // Unexpected value
        [OSSkipCondition(OperatingSystems.Windows, WindowsVersions.Win7, WindowsVersions.Win2008R2, SkipReason = "No WebSockets Client for this platform")]
        public async Task WebSocketsTransportThrowsForInvalidTransferFormat(TransferFormat transferFormat)
        {
            using (StartLog(out var loggerFactory))
            {
                var pair = DuplexPipe.CreateConnectionPair(PipeOptions.Default, PipeOptions.Default);
                var webSocketsTransport = new WebSocketsTransport(httpOptions: null, loggerFactory: loggerFactory);
                var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
                    webSocketsTransport.StartAsync(new Uri("http://fakeuri.org"), pair.Application, transferFormat, connection: Mock.Of<IConnection>()));

                Assert.Contains($"The '{transferFormat}' transfer format is not supported by this transport.", exception.Message);
                Assert.Equal("transferFormat", exception.ParamName);
            }
        }
    }
}
