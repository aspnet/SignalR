// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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

        private readonly Dictionary<string, int> _subscriptionsConnectionCount = new Dictionary<string, int>(StringComparer.Ordinal);

        private readonly ConcurrentDictionary<string, SubscriptionData> _subscriptions = new ConcurrentDictionary<string, SubscriptionData>(StringComparer.Ordinal);
        private readonly SemaphoreSlim _counterLock = new SemaphoreSlim(1, 1);

        public async Task AddSubscriptionAsync(string id, HubConnectionContext connection, Func<string, HubConnectionStore, Task> subscribeMethod)
        {
            await IncrementSubsciprionConnections(id);

            var subscription = _subscriptions.GetOrAdd(id, _ => new SubscriptionData());

            await subscription.Lock.WaitAsync();

            try
            {
                subscription.Connections.Add(connection);

                // Subscribe once
                if (subscription.Connections.Count == 1)
                {
                    await subscribeMethod(id, subscription.Connections);
                }
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

            await DecrementSubsciprionConnections(id);
        }

        private async Task IncrementSubsciprionConnections(string id)
        {
            await _counterLock.WaitAsync();
            try
            {
                //the idea to register an intention to use subscription data as soon as possible in global lock
                //so other connections won't remove it while we use subscription lock
                _subscriptionsConnectionCount.TryGetValue(id, out var count);
                _subscriptionsConnectionCount[id] = ++count;
            }
            finally
            {
                _counterLock.Release();
            }
        }

        private async Task DecrementSubsciprionConnections(string id)
        {
            await _counterLock.WaitAsync();
            try
            {
                if (_subscriptionsConnectionCount.TryGetValue(id, out var count))
                {
                    _subscriptionsConnectionCount[id] = --count;
                }
                if (count == 0) //no connection intents to use this subscription we are free to remove it
                {
                    _subscriptionsConnectionCount.Remove(id);
                    _subscriptions.TryRemove(id, out _);
                }
            }
            finally
            {
                _counterLock.Release();
            }
        }
    }
}
