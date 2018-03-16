// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Threading.Tasks;

namespace System.Threading.Channels
{
    public static class ChannelExtensions
    {
        public static async Task<List<T>> ReadAllAsync<T>(this ChannelReader<T> channel)
        {
            var list = new List<T>();

            await ReadAllIntoListAsync(channel, list);

            return list;
        }


        public static async Task ReadAllIntoListAsync<T>(this ChannelReader<T> channel, List<T> list)
        {
            while (await channel.WaitToReadAsync())
            {
                while (channel.TryRead(out var item))
                {
                    list.Add(item);
                }
            }

            // Manifest any error from channel.Completion (which should be completed now)
            await channel.Completion;
        }
    }
}
