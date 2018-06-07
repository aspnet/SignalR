using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Protocol;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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

        public async Task<string> UploadWord(object hackyWeaklyTypedParam)
        {
            var source = (ChannelReader<char>)hackyWeaklyTypedParam;
            //var source = hackyWeaklyTypedParam as ChannelReader<char>;
            var sb = new StringBuilder();

            // receiving a StreamCompleteMessage should cause this WaitToRead to return false
            while (await source.WaitToReadAsync())
            {
                while (source.TryRead(out var item))
                {
                    Console.WriteLine($"received: {item}");
                    sb.Append(item);
                }
            }

            // method returns, somewhere else returns a CompletionMessage with any errors
            return sb.ToString();
        }
    }
}
