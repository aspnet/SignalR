// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Reactive.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;

namespace SignalRSamples.Hubs
{
    public class Streaming : Hub
    {
        public ChannelReader<int> ObservableCounter(int count, int delay)
        {
            // This sample shows adapting an observable to a ChannelReader without 
            // back pressure, if the connection is slower than the producer, memory will
            // start to increase.
            var observable = Observable.Interval(TimeSpan.FromMilliseconds(delay))
                             .Select((_, index) => index)
                             .Take(count);

            var channel = Channel.CreateUnbounded<int>();

            var disposable = observable.Subscribe(
                                value => channel.Writer.TryWrite(value),
                                error => channel.Writer.TryComplete(error),
                                () => channel.Writer.TryComplete());

            // Complete the subscription on the reader completing
            channel.Reader.Completion.ContinueWith(task => disposable.Dispose());

            return channel.Reader;
        }

        public ChannelReader<int> ChannelCounter(int count, int delay)
        {
            var channel = Channel.CreateUnbounded<int>();

            Task.Run(async () =>
            {
                for (var i = 0; i < count; i++)
                {
                    await channel.Writer.WriteAsync(i);
                    await Task.Delay(delay);
                }

                channel.Writer.TryComplete();
            });

            return channel.Reader;
        }
    }
}
