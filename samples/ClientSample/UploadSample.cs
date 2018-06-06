using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.SignalR.Protocol;
using Microsoft.Extensions.CommandLineUtils;
using System;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace ClientSample
{
    class UploadSample
    {
        internal static void Register(CommandLineApplication app)
        {
            app.Command("uploading", cmd =>
            {
                cmd.Description = "Tests a streaming from client to hub";

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

            var channel = Channel.CreateUnbounded<char>();
            var uploadStream = connection.InvokeAsync<string>("UploadWord", channel.Reader);

            foreach (char c in "hello world")
            {
                await channel.Writer.WriteAsync(c);
                await Task.Delay(300);
            }

            channel.Writer.TryComplete();

            var result = await uploadStream;
            // check if it's errored

            return 0;
        }

        public static async Task<int> ExecuteAsyncTwo(string baseUrl)
        {
            var connection = new HubConnectionBuilder()
                .WithUrl(baseUrl)
                .Build();

            await connection.StartAsync();

            var channel = Channel.CreateUnbounded<char>();
            _ = WriteLettersAsync(channel.Writer);
            var result = await connection.InvokeAsync<string>("UploadWord", channel.Reader);

            // check if it's errored

            return 0;

            async Task WriteLettersAsync(ChannelWriter<char> writer)
            {
                foreach (char c in "hello world")
                {
                    await Task.Delay(1000);
                    await writer.WriteAsync(c);
                }
                writer.TryComplete(); // should send StreamCompleteMessage
            }
        }


        //public static async Task<int> ExecuteAsyncThree(string baseUrl)
        //{
        //    var connection = new HubConnectionBuilder().WithUrl(baseUrl).Build();

        //    await connection.StartAsync();

        //    var result = await connection.StreamToServerAsync<string>(
        //        source: WriteLettersAsync,
        //        channel: Channel.CreateUnbounded<char>(),
        //        target: "UploadWord"
        //    );

        //    return 0;

        //    async Task WriteLettersAsync(ChannelWriter<char> writer)
        //    {
        //        foreach (char c in "hello world")
        //        {
        //            await Task.Delay(1000);
        //            await writer.WriteAsync(c);
        //        }
        //    }
        //}
    }
}
