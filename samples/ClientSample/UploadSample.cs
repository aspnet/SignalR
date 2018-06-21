using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.CommandLineUtils;

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

                cmd.OnExecute(() => RunThatBoi(baseUrlArgument.Value));
            });
        }

        public static async Task<int> RunThatBoi(string baseUrl)
        {
            var connection = new HubConnectionBuilder()
                .WithUrl(baseUrl)
                .Build();
            await connection.StartAsync();

            await BasicRun(connection);
            //await AdditionalArgs(connection);
            //await InterleavedUploads(connection);
            //await FileUploadDemo(connection);

            return 0;
        }

        public static async Task BasicRun(HubConnection connection)
        {
            var channel = Channel.CreateUnbounded<string>();
            _ = WriteItemsAsync(channel.Writer);

            var result = await connection.InvokeAsync<string>("UploadWord", channel.Reader);
            Debug.WriteLine($"You message was: {result}");

            async Task WriteItemsAsync(ChannelWriter<string> source)
            {
                await Task.Delay(3000);
                foreach (char c in "hello")
                {
                    await source.WriteAsync(c.ToString());
                    await Task.Delay(500);
                }

                // tryComplete triggers the end of this upload's relayLoop
                // which sends a StreamComplete to the server
                source.TryComplete();
            }
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

        public static async Task FileUploadDemo(HubConnection connection)
        {
            
            var original = @"C:\Users\t-dygray\Desktop\uploads\sample.txt";
            var target = @"C:\Users\t-dygray\Desktop\uploads\bloop.txt";

            original = @"C:\Users\t-dygray\Pictures\weeg.jpg";
            target = @"C:\Users\t-dygray\Desktop\uploads\bloop.jpg";


            var channel = Channel.CreateUnbounded<byte[]>();
            _ = StreamFileAsync(channel.Writer);

            var result = await connection.InvokeAsync<string>("UploadFile", channel.Reader, target);
            Debug.WriteLine($"upload complete with status: {result}");

            async Task StreamFileAsync(ChannelWriter<byte[]> source)
            {
                await Task.Delay(1000);

                using (FileStream fs = File.OpenRead(original))
                {
                    byte[] b = new byte[8 * 1024];

                    while (true)
                    {
                        var n = fs.Read(b, 0, b.Length);
                        if (n < b.Length) {
                            // on the last cycle, there may not be enough to fill the buffer completely
                            // in this case, we want to empty the buffer completely before refilling it
                            byte[] lastbit = new byte[n];
                            Array.Copy(b, lastbit, n);

                            await source.WriteAsync(lastbit);

                            break;
                        }

                        await source.WriteAsync(b);
                        await Task.Delay(500);
                    }

                    

                }

                // should be good
                source.TryComplete();
            }
        }
    }
}

