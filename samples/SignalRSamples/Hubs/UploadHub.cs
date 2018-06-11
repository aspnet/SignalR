using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Protocol;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace SignalRSamples.Hubs
{
    public class UploadHub : Hub
    {
        public int TraceMethod(int i)
        {
            Debug.WriteLine("Ope");
            return i;
        }

        public async Task<string> UploadWord(ChannelReader<string> source)
        {
            //var source = hackyWeaklyTypedParam as ChannelReader<char>;
            var sb = new StringBuilder();

            // receiving a StreamCompleteMessage should cause this WaitToRead to return false
            while (await source.WaitToReadAsync())
            {
                while (source.TryRead(out var item))
                {
                    Debug.WriteLine($"received: {item}");
                    Console.WriteLine($"received: {item}");
                    sb.Append(item);
                }
            }

            // method returns, somewhere else returns a CompletionMessage with any errors
            return sb.ToString();
        }

        public async Task<string> UploadWithSuffix(ChannelReader<string> source, string suffix)
        {
            var sb = new StringBuilder();

            while (await source.WaitToReadAsync())
            {
                while (source.TryRead(out var item))
                {
                    await Task.Delay(50);
                    Debug.WriteLine($"received: {item}");
                    sb.Append(item);
                }
            }

            sb.Append(suffix);

            return sb.ToString();
        }

        public async Task<string> UploadFile(ChannelReader<byte[]> source, string filepath)
        {
            var result = Enumerable.Empty<byte>();
            int chunk = 1;

            while (await source.WaitToReadAsync())
            {
                while (source.TryRead(out var item))
                {
                    //File.WriteAllBytes($@"C:\Users\t-dygray\Desktop\uploads\bloop_{chunk}.txt", item);

                    Debug.WriteLine($"received chunk #{chunk++}");
                    result = result.Concat(item);  // atrocious
                    await Task.Delay(50);
                }
            }

            File.WriteAllBytes(filepath, result.ToArray());

            Debug.WriteLine("returning status code");
            return $"file written to '{filepath}'";
        }
    }
}
