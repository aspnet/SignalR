// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Threading.Tasks.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Sockets.Internal;
using System.Threading;

namespace Microsoft.AspNetCore.Sockets.Client
{
    public class Connection : IChannelConnection<Message>
    {
        private class ConnectionState
        {
            public const int Disconnected = 0;
            public const int Connecting = 1;
            public const int Connected = 2;
        }

        private int _connectionState;
        private IChannelConnection<Message> _transportChannel;
        private ITransport _transport;
        private bool _ownsTransport;
        private readonly ILogger _logger;
        private readonly ILoggerFactory _loggerFactory;

        public Uri Url { get; }

        public Connection(Uri url)
            : this(url, null)
        { }

        public Connection(Uri url, ILoggerFactory loggerFactory)
        {
            if (url == null)
            {
                throw new ArgumentNullException(nameof(url));
            }

            Url = url;
            _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
            _logger = _loggerFactory.CreateLogger<Connection>();
        }

        public ReadableChannel<Message> Input => _transportChannel.Input;
        public WritableChannel<Message> Output => _transportChannel.Output;

        public Task StartAsync()
        {
            return StartAsync(null, null);
        }

        public Task StartAsync(ITransport transport)
        {
            return StartAsync(transport, null);
        }

        public async Task StartAsync(ITransport transport, HttpClient httpClient)
        {
            if (Interlocked.CompareExchange(ref _connectionState, ConnectionState.Connecting, ConnectionState.Disconnected)
                != ConnectionState.Disconnected)
            {
                throw new InvalidOperationException("Cannot start an already running connection.");
            }

            if (httpClient == null)
            {
                // TODO: httpClient needs to be disposed properly (after long polling is not the default transport)
                httpClient = new HttpClient();
            }

            var connectionId = await Negotiate(httpClient);

            var connectedUrl = Utils.AppendQueryString(Url, "id=" + connectionId);
            var applicationToTransport = Channel.CreateUnbounded<Message>();
            var transportToApplication = Channel.CreateUnbounded<Message>();
            var applicationSide = new ChannelConnection<Message>(transportToApplication, applicationToTransport);
            _transportChannel = new ChannelConnection<Message>(applicationToTransport, transportToApplication);

            try
            {
                // Start the transport, giving it one end of the pipeline
                _transport = transport ?? new LongPollingTransport(_loggerFactory, httpClient);
                _ownsTransport = transport == null;
                await _transport.StartAsync(connectedUrl, applicationSide);
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to start connection. Error starting transport '{0}': {1}", _transport.GetType().Name, ex);
                Interlocked.Exchange(ref _connectionState, ConnectionState.Disconnected);
                throw;
            }

            Interlocked.Exchange(ref _connectionState, ConnectionState.Connected);
        }

        private async Task<string> Negotiate(HttpClient httpClient)
        {
            var negotiateUrl = Utils.AppendPath(Url, "negotiate");

            string connectionId;
            try
            {
                // Get a connection ID from the server
                _logger.LogDebug("Establishing Connection at: {0}", negotiateUrl);
                connectionId = await httpClient.GetStringAsync(negotiateUrl);
                _logger.LogDebug("Connection Id: {0}", connectionId);
            }
            catch (Exception ex)
            {
                Interlocked.Exchange(ref _connectionState, ConnectionState.Disconnected);
                _logger.LogError("Failed to start connection. Error getting connection id from '{0}': {1}", negotiateUrl, ex);
                throw;
            }

            return connectionId;
        }

        public async Task StopAsync()
        {
            if (_transport != null)
            {
                await _transport.StopAsync();
            }

            Interlocked.Exchange(ref _connectionState, ConnectionState.Disconnected);
        }

        public void Dispose()
        {
            if (_ownsTransport && _transport != null)
            {
                _transport.Dispose();
            }

            Interlocked.Exchange(ref _connectionState, ConnectionState.Disconnected);
        }
    }
}
