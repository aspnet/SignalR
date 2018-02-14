// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Sockets.Client;
using Microsoft.Extensions.CommandLineUtils;
using Microsoft.Extensions.Logging;

namespace ClientSample
{
    internal class RawSample
    {
        internal static void Register(CommandLineApplication app)
        {
            app.Command("raw", cmd =>
            {
                cmd.Description = "Tests a connection to an endpoint";

                var baseUrlArgument = cmd.Argument("<BASEURL>", "The URL to the Chat EndPoint to test");

                cmd.OnExecute(() => ExecuteAsync(baseUrlArgument.Value));
            });
        }

        public static async Task<int> ExecuteAsync(string baseUrl)
        {
            baseUrl = string.IsNullOrEmpty(baseUrl) ? "http://localhost:5000/chat" : baseUrl;

            var loggerFactory = new LoggerFactory();
            var logger = loggerFactory.CreateLogger<Program>();

            Console.WriteLine($"Connecting to {baseUrl}...");
            var connection = new HttpConnection(new Uri(baseUrl), loggerFactory);
            Task reading = null;

            try
            {
                var closeTcs = new TaskCompletionSource<object>();
                connection.Closed += e => closeTcs.SetResult(null);

                await connection.StartAsync();

                reading = ReadAsync(connection);

                Console.WriteLine($"Connected to {baseUrl}");
                var cts = new CancellationTokenSource();
                Console.CancelKeyPress += async (sender, a) =>
                {
                    a.Cancel = true;
                    await connection.DisposeAsync();
                };

                while (!closeTcs.Task.IsCompleted)
                {
                    var line = await Task.Run(() => Console.ReadLine(), cts.Token);

                    if (line == null)
                    {
                        break;
                    }

                    await connection.Output.WriteAsync(Encoding.UTF8.GetBytes(line));
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

                connection.Output.Complete();

                if (reading != null)
                {
                    await reading;
                }
            }
            return 0;
        }

        private static async Task ReadAsync(HttpConnection connection)
        {
            while (true)
            {
                var result = await connection.Input.ReadAsync();
                var buffer = result.Buffer;

                try
                {
                    if (!buffer.IsEmpty)
                    {
                        Console.WriteLine(Encoding.UTF8.GetString(buffer.ToArray()));
                    }
                    else if (result.IsCompleted)
                    {
                        break;
                    }
                }
                finally
                {
                    connection.Input.AdvanceTo(buffer.End);
                }
            }

            connection.Input.Complete();
        }
    }
}
