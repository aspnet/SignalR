// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Threading.Tasks.Channels;
using Microsoft.AspNetCore.Sockets.Internal;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Sockets.Client
{
    public class Connection : IChannelConnection<Message>
    {
        private IChannelConnection<Message> _toFromTransport;
        private ITransport _transport;
        private readonly ILogger _logger;

        public Uri Url { get; }

        // TODO: Review. This is really only designed to be used from ConnectAsync
        private Connection(Uri url, ITransport transport, IChannelConnection<Message> toFromTransport, ILogger logger)
        {
            Url = url;

            _logger = logger;
            _transport = transport;
            _toFromTransport = toFromTransport;
        }

        public ReadableChannel<Message> Input => _toFromTransport.Input;
        public WritableChannel<Message> Output => _toFromTransport.Output;

        public void Dispose()
        {
            _transport.Dispose();
        }

        public static Task<Connection> ConnectAsync(Uri url, ITransport transport) => ConnectAsync(url, transport, new HttpClient(), NullLoggerFactory.Instance);
        public static Task<Connection> ConnectAsync(Uri url, ITransport transport, ILoggerFactory loggerFactory) => ConnectAsync(url, transport, new HttpClient(), loggerFactory);
        public static Task<Connection> ConnectAsync(Uri url, ITransport transport, HttpClient httpClient) => ConnectAsync(url, transport, httpClient, NullLoggerFactory.Instance);

        public static async Task<Connection> ConnectAsync(Uri url, ITransport transport, HttpClient httpClient, ILoggerFactory loggerFactory)
        {
            if (url == null)
            {
                throw new ArgumentNullException(nameof(url));
            }

            if (transport == null)
            {
                throw new ArgumentNullException(nameof(transport));
            }

            if (httpClient == null)
            {
                throw new ArgumentNullException(nameof(httpClient));
            }

            if (loggerFactory == null)
            {
                throw new ArgumentNullException(nameof(loggerFactory));
            }

            var logger = loggerFactory.CreateLogger<Connection>();
            var negotiateUrl = Utils.AppendPath(url, "negotiate");

            string connectionId;
            try
            {
                // Get a connection ID from the server
                logger.LogDebug("Establishing Connection at: {0}", negotiateUrl);
                connectionId = await httpClient.GetStringAsync(negotiateUrl);
                logger.LogDebug("Connection Id: {0}", connectionId);
            }
            catch (Exception ex)
            {
                logger.LogError("Failed to start connection. Error getting connection id from '{0}': {1}", negotiateUrl, ex);
                throw;
            }

            var connectedUrl = Utils.AppendQueryString(url, "id=" + connectionId);

            var connectionToTransport = Channel.CreateUnbounded<Message>();
            var transportToConnection = Channel.CreateUnbounded<Message>();
            var toFromConnection = new ChannelConnection<Message>(transportToConnection, connectionToTransport);
            var toFromTransport = new ChannelConnection<Message>(connectionToTransport, transportToConnection);


            // Start the transport, giving it one end of the pipeline
            try
            {
                await transport.StartAsync(connectedUrl, toFromConnection);
            }
            catch (Exception ex)
            {
                logger.LogError("Failed to start connection. Error starting transport '{0}': {1}", transport.GetType().Name, ex);
                throw;
            }

            // Create the connection, giving it the other end of the pipeline
            return new Connection(url, transport, toFromTransport, logger);
        }
    }
}
