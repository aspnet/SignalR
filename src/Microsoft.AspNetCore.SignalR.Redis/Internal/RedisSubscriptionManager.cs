// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.SignalR.Redis.Internal
{
    internal class RedisSubscriptionManager
    {
        private readonly ConcurrentDictionary<string, HubConnectionStore> _subscriptions = new ConcurrentDictionary<string, HubConnectionStore>(StringComparer.Ordinal);
        private readonly object _lock = new object();

        public Task AddSubscriptionAsync(string id, HubConnectionContext connection, Func<string, HubConnectionStore, Task> subscribeMethod)
        {
            var firstSubscription = false;
            HubConnectionStore subscription;
            lock (_lock)
            {
                subscription = _subscriptions.GetOrAdd(id, _ => new HubConnectionStore());

                // Subscribe once
                if (subscription.Count == 1)
                {
                    firstSubscription = true;
                }
            }

            if (firstSubscription)
            {
                return subscribeMethod(id, subscription);
            }

            return Task.CompletedTask;
        }

        public Task RemoveSubscriptionAsync(string id, HubConnectionContext connection, Func<string, Task> unsubscribeMethod)
        {
            var removeSubscription = false;

            lock (_lock)
            {
                if (!_subscriptions.TryGetValue(id, out var subscription))
                {
                    return Task.CompletedTask;
                }

                subscription.Remove(connection);

                if (subscription.Count == 0)
                {
                    _subscriptions.TryRemove(id, out _);
                    removeSubscription = true;
                }
            }

            if (removeSubscription)
            {
                return unsubscribeMethod(id);
            }

            return Task.CompletedTask;
        }
    }
}
