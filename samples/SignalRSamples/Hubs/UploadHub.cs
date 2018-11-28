﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;

namespace SignalRSamples.Hubs
{
    public class UploadHub : Hub
    {

        public string Echo(string word)
        {
            return "Echo: " + word;
        }

        public async Task<string> UploadWord(ChannelReader<string> source)
        {
            var sb = new StringBuilder();

            // receiving a StreamCompleteMessage should cause this WaitToRead to return false
            while (await source.WaitToReadAsync())
            {
                while (source.TryRead(out string item))
                {
                    Debug.WriteLine($"received: {item}");
                    Console.WriteLine($"received: {item}");
                    sb.Append(item);
                }
            }

            // method returns, somewhere else returns a CompletionMessage with any errors
            return sb.ToString();
        }

        public async Task<string> ScoreTracker(ChannelReader<int> player1, ChannelReader<int> player2)
        {
            var p1score = await Loop(player1);
            var p2score = await Loop(player2);

            var winner = p1score > p2score ? "p1" : "p2";
            return $"{winner} wins with a total of {Math.Max(p1score, p2score)} points to {Math.Min(p1score, p2score)}"; 

            async Task<int> Loop(ChannelReader<int> reader)
            {
                var score = 0;

                while (await reader.WaitToReadAsync())
                {
                    while (reader.TryRead(out int item))
                    {
                        Debug.WriteLine($"got score {item}");
                        score += item;
                    }
                }

                return score;
            }
        }

        public async Task UploadFile(string filepath, ChannelReader<byte[]> source)
        {
            var result = Enumerable.Empty<byte>();
            var chunk = 1;

            while (await source.WaitToReadAsync())
            {
                while (source.TryRead(out var item))
                {
                    Debug.WriteLine($"received chunk #{chunk++}");
                    result = result.Concat(item);  // atrocious
                    await Task.Delay(50);
                }
            }

            File.WriteAllBytes(filepath, result.ToArray());
        }

        public ChannelReader<string> StreamEcho(ChannelReader<string> source)
        {
            var output = Channel.CreateUnbounded<string>();

            _ = Task.Run(async () =>
            {
                while (await source.WaitToReadAsync())
                {
                    while (source.TryRead(out var item))
                    {
                        Debug.WriteLine($"Echoing '{item}'.");
                        await output.Writer.WriteAsync("echo:" + item);
                    }
                }
                output.Writer.Complete();

            });

            return output.Reader;
        }
    }
}
