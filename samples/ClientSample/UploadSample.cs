using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.SignalR.Protocol;
using Microsoft.Extensions.CommandLineUtils;
using System;
using System.Diagnostics;
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

                cmd.OnExecute(() => RealBasicRun(baseUrlArgument.Value));
            });
        }

        public static async Task<int> TestStreamItemSend(string baseUrl)
        {
            var connection = new HubConnectionBuilder()
                .WithUrl(baseUrl)
                .Build();
            await connection.StartAsync();

            await connection.SendStreamItemCoreAsyncCore(new StreamItemMessage("322", "ggez"));

            Debug.WriteLine("done");

            return 0;
        }

        public static async Task<int> RealBasicRun(string baseUrl)
        {
            var connection = new HubConnectionBuilder()
                .WithUrl(baseUrl)
                .Build();
            await connection.StartAsync();

            var channel = Channel.CreateUnbounded<char>();
            await connection.InvokeAsync<string>("UploadWord", channel.Reader);

            await Task.Delay(5000);
            foreach (char c in "glhf")
            {
                await connection.SendStreamItemCoreAsyncCore(new StreamItemMessage("1", c));
                await Task.Delay(20000);
                Debug.WriteLine("oof sent an item");
                await Task.Delay(135000);
            }

            return 0;
        }

        public static async Task<int> ExecuteAsync(string baseUrl)
        {
            var connection = new HubConnectionBuilder()
                .WithUrl(baseUrl)
                .Build();

            await connection.StartAsync();

            //var response = await connection.InvokeAsync<int>("TraceMethod", 5);
            //Debug.Write($"Trace Done, response <{response}> should be 5.");

            var channel = Channel.CreateUnbounded<char>();
            var uploadStream = connection.InvokeAsync<string>("UploadWord", channel.Reader);

            await Task.Delay(5000);
            foreach (char c in "glhf")
            {
                await channel.Writer.WriteAsync(c);
                await Task.Delay(20000);
                Debug.WriteLine("oof sent an item");
                await Task.Delay(135000);
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
