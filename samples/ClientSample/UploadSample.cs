// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.CommandLineUtils;

namespace ClientSample
{
    internal class UploadSample
    {
        internal static void Register(CommandLineApplication app)
        {
            app.Command("uploading", cmd =>
            {
                cmd.Description = "Tests a streaming invocation from client to hub";

                var baseUrlArgument = cmd.Argument("<BASEURL>", "The URL to the Chat Hub to test");

                cmd.OnExecute(() => ExecuteAsync(baseUrlArgument.Value));
            });
        }

        public static async Task<int> ExecuteAsync(string baseUrl)
        {
            var connection = new HubConnectionBuilder()
                .WithUrl(baseUrl)
                .Build();
            await connection.StartAsync();

            //await BasicInvoke(connection);
            //await MultiParamInvoke(connection);
            //await BasicSend(connection);
            //await AdditionalArgs(connection);
            await InterleavedUploads(connection);

            return 0;
        }

        public static async Task BasicInvoke(HubConnection connection)
        {
            var channel = Channel.CreateUnbounded<string>();
            var invokeTask = connection.InvokeAsync<string>("UploadWord", channel.Reader);

            foreach (var c in "hello")
            {
                await channel.Writer.WriteAsync(c.ToString());
            }
            channel.Writer.TryComplete();

            var result = await invokeTask;
            Debug.WriteLine($"You message was: {result}");
        }

        private static async Task WriteStreamAsync<T>(IEnumerable<T> sequence, ChannelWriter<T> writer)
        {
            foreach (T element in sequence)
            {
                await writer.WriteAsync(element);
                await Task.Delay(100);
            }

            writer.TryComplete();
        }

        public static async Task MultiParamInvoke(HubConnection connection)
        {
            var letters = Channel.CreateUnbounded<string>();
            var numbers = Channel.CreateUnbounded<int>();

            _ = WriteStreamAsync(new[] { "h", "i", "!" }, letters.Writer);
            _ = WriteStreamAsync(new[] { 1, 2, 3, 4, 5 }, numbers.Writer);

            var result = await connection.InvokeAsync<string>("DoubleStreamUpload", letters.Reader, numbers.Reader);

            Debug.WriteLine(result);
        }

        public static async Task BasicSend(HubConnection connection)
        {
            //var letters = Channel.CreateUnbounded<string>();
            var numbers = Channel.CreateUnbounded<int>();

            // we can call the boy from here, since there's no need to wait on ~"completion"~
            await connection.SendAsync("LocalSum", numbers.Reader);

            _ = WriteStreamAsync(new[] { 37, 2, 3 }, numbers.Writer);

            await Task.Delay(3000);
            // the server will "Debug.WriteLine"
        }

        public static async Task AdditionalArgs(HubConnection connection)
        {
            var channel = Channel.CreateUnbounded<string>();
            _ = WriteItemsAsync(channel.Writer);

            var result = await connection.InvokeAsync<string>("UploadWithSuffix", channel.Reader, " + wooh I'm a suffix");
            Debug.WriteLine($"Your message was: {result}");

            async Task WriteItemsAsync(ChannelWriter<string> source)
            {
                await Task.Delay(1000);
                foreach (char c in "streamed stuff")
                {
                    await source.WriteAsync(c.ToString());
                    await Task.Delay(500);
                }

                // tryComplete triggers the end of this upload's relayLoop
                // which sends a StreamComplete to the server
                source.TryComplete();
            }
        }

        public static async Task InterleavedUploads(HubConnection connection)
        {
            var channel_one = Channel.CreateBounded<string>(2);
            _ = WriteItemsAsync(channel_one.Writer, "first message");
            var taskOne = connection.InvokeAsync<string>("UploadWord", channel_one.Reader);

            var channel_two = Channel.CreateBounded<string>(2);
            _ = WriteItemsAsync(channel_two.Writer, "second message");
            var taskTwo = connection.InvokeAsync<string>("UploadWord", channel_two.Reader);


            var result_one = await taskOne;
            var result_two = await taskTwo;

            Debug.WriteLine($"MESSAGES: '{result_one}', '{result_two}'");


            async Task WriteItemsAsync(ChannelWriter<string> source, string data)
            {
                await Task.Delay(1000);
                foreach (char c in data)
                {
                    await source.WriteAsync(c.ToString());
                    await Task.Delay(250);
                }

                // tryComplete triggers the end of this upload's relayLoop
                // which sends a StreamComplete to the server
                source.TryComplete();
            }
        }
    }
}

