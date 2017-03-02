﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Sockets.Client;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Sockets;
using System.Linq;
using System.Diagnostics;

namespace ClientSample
{
    internal class RawSample
    {
        public static async Task MainAsync(string[] args)
        {
            if(args.Any(a => a == "--debug"))
            {
                Console.WriteLine($"Ready for debugger to attach. Process ID: {Process.GetCurrentProcess().Id}");
                Console.Write("Press ENTER to Continue");
                Console.ReadLine();
                args = args.Where(a => a != "--debug").ToArray();
            }

            var baseUrl = "http://localhost:5000/chat";
            if (args.Length > 0)
            {
                baseUrl = args[0];
            }

            var loggerFactory = new LoggerFactory();
            loggerFactory.AddConsole(LogLevel.Debug);
            var logger = loggerFactory.CreateLogger<Program>();

            using (var httpClient = new HttpClient(new LoggingMessageHandler(loggerFactory, new HttpClientHandler())))
            {
                logger.LogInformation("Connecting to {0}", baseUrl);
                var transport = new LongPollingTransport(httpClient, loggerFactory);
                var connection = new Connection(new Uri(baseUrl), loggerFactory);
                try
                {
                    var cts = new CancellationTokenSource();
                    connection.Received += (data, format) => logger.LogInformation($"Received: {Encoding.UTF8.GetString(data)}");
                    connection.Closed += e => cts.Cancel();

                    await connection.StartAsync(transport, httpClient);

                    logger.LogInformation("Connected to {0}", baseUrl);

                    Console.CancelKeyPress += (sender, a) =>
                    {
                        a.Cancel = true;
                        logger.LogInformation("Stopping loops...");
                        cts.Cancel();
                    };

                    await StartSending(loggerFactory.CreateLogger("SendLoop"), connection, cts.Token).ContinueWith(_ => cts.Cancel());
                }
                finally
                {
                    await connection.DisposeAsync();
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

                await connection.SendAsync(Encoding.UTF8.GetBytes("Hello World"), MessageType.Text);
            }
            logger.LogInformation("Send loop terminated");
        }
    }
}
