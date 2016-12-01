﻿using System;
using System.IO.Pipelines;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Sockets.Client;
using Microsoft.Extensions.Logging;

namespace ClientSample
{
    internal class RawSample
    {
        public static async Task MainAsync(string[] args)
        {
            var baseUrl = "http://localhost:5000/chat";
            if (args.Length > 0)
            {
                baseUrl = args[0];
            }

            var loggerFactory = new LoggerFactory();
            loggerFactory.AddConsole(LogLevel.Debug);
            var logger = loggerFactory.CreateLogger<Program>();

            using (var httpClient = new HttpClient(new LoggingMessageHandler(loggerFactory, new HttpClientHandler())))
            using (var pipelineFactory = new PipelineFactory())
            {
                logger.LogInformation("Connecting to {0}", baseUrl);
                var transport = new LongPollingTransport(httpClient, loggerFactory);
                using (var connection = await Connection.ConnectAsync(new Uri(baseUrl), transport, httpClient, pipelineFactory, loggerFactory))
                {
                    logger.LogInformation("Connected to {0}", baseUrl);

                    var cts = new CancellationTokenSource();
                    Console.CancelKeyPress += (sender, a) =>
                    {
                        a.Cancel = true;
                        logger.LogInformation("Stopping loops...");
                        cts.Cancel();
                    };

                    // Ready to start the loops
                    var receive = StartReceiving(loggerFactory.CreateLogger("ReceiveLoop"), connection, cts.Token);
                    var send = StartSending(loggerFactory.CreateLogger("SendLoop"), connection, cts.Token);

                    await Task.WhenAll(receive, send);
                }
            }
        }

        private static async Task StartSending(ILogger logger, Connection connection, CancellationToken cancellationToken)
        {
            logger.LogInformation("Send loop starting");
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = Console.ReadLine();
                logger.LogInformation("Sending: {0}", line);

                await connection.Output.WriteAsync(Encoding.UTF8.GetBytes(line));
            }
            logger.LogInformation("Send loop terminated");
        }

        private static async Task StartReceiving(ILogger logger, Connection connection, CancellationToken cancellationToken)
        {
            logger.LogInformation("Receive loop starting");
            using (cancellationToken.Register(() => connection.Input.Complete()))
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var result = await connection.Input.ReadAsync();
                    var buffer = result.Buffer;
                    try
                    {
                        if (!buffer.IsEmpty)
                        {
                            var message = Encoding.UTF8.GetString(buffer.ToArray());
                            logger.LogInformation("Received: {0}", message);
                        }
                    }
                    finally
                    {
                        connection.Input.Advance(buffer.End);
                    }
                    if (result.IsCompleted)
                    {
                        break;
                    }
                }
            }
            logger.LogInformation("Receive loop terminated");
        }
    }
}
