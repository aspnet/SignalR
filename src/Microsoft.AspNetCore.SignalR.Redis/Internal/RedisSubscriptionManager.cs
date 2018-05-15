// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.SignalR.Redis.Internal
{
    internal class RedisSubscriptionManager
    {
        private class SubscriptionData
        {
            public readonly SemaphoreSlim Lock = new SemaphoreSlim(1, 1);
            public readonly HubConnectionStore Connections = new HubConnectionStore();
        }

        // TODO: Investigate "memory leak" entries never get removed
        private readonly ConcurrentDictionary<string, SubscriptionData> _subscriptions = new ConcurrentDictionary<string, SubscriptionData>(StringComparer.Ordinal);

        public async Task AddSubscriptionAsync(string id, HubConnectionContext connection, Func<string, HubConnectionStore, Task> subscribeMethod)
        {
            var subscription = _subscriptions.GetOrAdd(id, _ => new SubscriptionData());

            await subscription.Lock.WaitAsync();

            try
            {
                subscription.Connections.Add(connection);

                // Subscribe once
                if (subscription.Connections.Count > 1)
                {
                    return;
                }

                await subscribeMethod(id, subscription.Connections);
            }
            finally
            {
                subscription.Lock.Release();
            }
        }

        public async Task RemoveSubscriptionAsync(string id, HubConnectionContext connection, Func<string, Task> unsubscribeMethod)
        {
            if (!_subscriptions.TryGetValue(id, out var subscription))
            {
                return;
            }

            await subscription.Lock.WaitAsync();

            try
            {
                if (subscription.Connections.Count > 0)
                {
                    subscription.Connections.Remove(connection);

                    if (subscription.Connections.Count == 0)
                    {
                        await unsubscribeMethod(id);
                    }
                }
            }
            finally
            {
                subscription.Lock.Release();
            }
        }
    }
}
