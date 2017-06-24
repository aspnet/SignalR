// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.SignalR.Redis;
using Microsoft.Extensions.CommandLineUtils;
using Microsoft.Extensions.Logging;

namespace ClientSample
{
    internal class RedisHubSample
    {
        internal static void Register(CommandLineApplication app)
        {
            app.Command("redis", cmd =>
            {
                cmd.Description = "Tests a connection to a hub via redis";

                cmd.OnExecute(() => ExecuteAsync());
            });
        }

        public static async Task<int> ExecuteAsync()
        {
            var loggerFactory = new LoggerFactory();

            Console.WriteLine("Connecting to redis");
            var redisConnection = new RedisConnection("channel");

            var connection = new HubConnection(redisConnection, loggerFactory);
            try
            {
                await connection.StartAsync();
                Console.WriteLine("Connected to redis");

                var cts = new CancellationTokenSource();
                Console.CancelKeyPress += (sender, a) =>
                {
                    a.Cancel = true;
                    Console.WriteLine("Stopping loops...");
                    cts.Cancel();
                };

                // Set up handler
                connection.On<string>("Send", Console.WriteLine);

                while (!cts.Token.IsCancellationRequested)
                {
                    var line = await Task.Run(() => Console.ReadLine(), cts.Token);

                    if (line == null)
                    {
                        break;
                    }

                    await connection.Invoke("Send", cts.Token, line);
                }
            }
            catch (AggregateException aex) when (aex.InnerExceptions.All(e => e is OperationCanceledException))
            {
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                await connection.DisposeAsync();
            }
            return 0;
        }
    }
}
