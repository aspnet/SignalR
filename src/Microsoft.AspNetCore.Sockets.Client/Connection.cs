﻿using System;
using System.IO.Pipelines;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Sockets.Client
{
    public class Connection : IPipelineConnection
    {
        private IPipelineConnection _consumerPipe;
        private ITransport _transport;
        private readonly ILogger _logger;

        public Uri Url { get; }

        // TODO: Review. This is really only designed to be used from ConnectAsync
        private Connection(Uri url, ITransport transport, IPipelineConnection consumerPipe, ILogger logger)
        {
            Url = url;

            _logger = logger;
            _transport = transport;
            _consumerPipe = consumerPipe;
        }

        public IPipelineReader Input => _consumerPipe.Input;
        public IPipelineWriter Output => _consumerPipe.Output;

        public void Dispose()
        {
            _consumerPipe.Dispose();
            _transport.Dispose();
        }

        // TODO: More overloads. PipelineFactory should be optional but someone needs to dispose the pool, if we're OK with it being the GC, then this is easy.
        public static Task<Connection> ConnectAsync(Uri url, ITransport transport, PipelineFactory pipelineFactory) => ConnectAsync(url, transport, new HttpClient(), pipelineFactory, NullLoggerFactory.Instance);
        public static Task<Connection> ConnectAsync(Uri url, ITransport transport, PipelineFactory pipelineFactory, ILoggerFactory loggerFactory) => ConnectAsync(url, transport, new HttpClient(), pipelineFactory, loggerFactory);
        public static Task<Connection> ConnectAsync(Uri url, ITransport transport, HttpClient httpClient, PipelineFactory pipelineFactory) => ConnectAsync(url, transport, httpClient, pipelineFactory, NullLoggerFactory.Instance);

        public static async Task<Connection> ConnectAsync(Uri url, ITransport transport, HttpClient httpClient, PipelineFactory pipelineFactory, ILoggerFactory loggerFactory)
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

            if (pipelineFactory == null)
            {
                throw new ArgumentNullException(nameof(pipelineFactory));
            }

            if (loggerFactory == null)
            {
                throw new ArgumentNullException(nameof(loggerFactory));
            }

            var logger = loggerFactory.CreateLogger<Connection>();
            var getIdUrl = Utils.AppendPath(url, "getid");

            string connectionId;
            try
            {
                // Get a connection ID from the server
                logger.LogDebug("Reserving Connection Id from: {0}", getIdUrl);
                connectionId = await httpClient.GetStringAsync(getIdUrl);
                logger.LogDebug("Reserved Connection Id: {0}", connectionId);
            }
            catch (Exception ex)
            {
                logger.LogError("Failed to start connection. Error getting connection id from '{0}': {1}", getIdUrl, ex);
                throw;
            }

            var connectedUrl = Utils.AppendQueryString(url, "id=" + connectionId);

            var pair = pipelineFactory.CreatePipelinePair();

            // Start the transport, giving it one end of the pipeline
            try
            {
                await transport.StartAsync(connectedUrl, pair.Item1);
            }
            catch (Exception ex)
            {
                logger.LogError("Failed to start connection. Error starting transport '{0}': {1}", transport.GetType().Name, ex);
                throw;
            }

            // Create the connection, giving it the other end of the pipeline
            return new Connection(url, transport, pair.Item2, logger);
        }
    }
}
