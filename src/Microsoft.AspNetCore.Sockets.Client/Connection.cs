// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Threading.Tasks.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Sockets.Internal;

namespace Microsoft.AspNetCore.Sockets.Client
{
    public class Connection : IChannelConnection<Message>
    {
        private IChannelConnection<Message> _transportChannel;
        private ITransport _transport;
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
            // TODO: prevent from starting the connection if it has finished or is already running
            if (httpClient == null)
            {
                httpClient = new HttpClient();
            }

            _transport = transport ?? new LongPollingTransport(httpClient, _loggerFactory);

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
                _logger.LogError("Failed to start connection. Error getting connection id from '{0}': {1}", negotiateUrl, ex);
                throw;
            }

            var connectedUrl = Utils.AppendQueryString(Url, "id=" + connectionId);

            var applicationToTransport = Channel.CreateUnbounded<Message>();
            var transportToApplication = Channel.CreateUnbounded<Message>();
            var applicationSide = new ChannelConnection<Message>(transportToApplication, applicationToTransport);
            _transportChannel = new ChannelConnection<Message>(applicationToTransport, transportToApplication);

            // Start the transport, giving it one end of the pipeline
            try
            {
                await _transport.StartAsync(connectedUrl, applicationSide);
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to start connection. Error starting transport '{0}': {1}", _transport.GetType().Name, ex);
                throw;
            }
        }

        public void Stop()
        {
            if (_transport != null)
            {
                // TODO: should we just stop the transport?
                _transport.Dispose();
            }
        }

        public void Dispose()
        {
            // TODO: dispose httpclient?
            Stop();
        }
    }
}
