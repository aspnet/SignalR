// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.SignalR.Protocol;

namespace Microsoft.AspNetCore.SignalR
{
    /// <summary>
    /// This class is designed to support the framework. The API is subject to breaking changes.
    /// Represents a serialization cache for a single message.
    /// </summary>
    public class SerializedHubMessage
    {
        private SerializedMessage _cachedItem1;
        private SerializedMessage _cachedItem2;
        private IList<SerializedMessage> _cachedItems;
        private readonly object _lock = new object();
        private int _count = 0;

        public HubMessage Message { get; }

        public SerializedHubMessage(IReadOnlyList<SerializedMessage> messages)
        {
            for (var i = 0; i < messages.Count; i++)
            {
                var message = messages[i];
                SetCache(message.ProtocolName, message.Serialized);
            }
        }

        public SerializedHubMessage(HubMessage message)
        {
            Message = message;
        }

        public ReadOnlyMemory<byte> GetSerializedMessage(IHubProtocol protocol)
        {
            // Double-check locking!
            if (!TryGetCachedFast(protocol.Name, out var serialized))
            {
                lock (_lock)
                {
                    if (!TryGetCached(protocol.Name, out serialized))
                    {
                        if (Message == null)
                        {
                            throw new InvalidOperationException(
                                "This message was received from another server that did not have the requested protocol available.");
                        }

                        serialized = protocol.GetMessageBytes(Message);
                        SetCache(protocol.Name, serialized);
                    }
                }
            }

            return serialized;
        }

        // Used for unit testing.
        internal IEnumerable<SerializedMessage> GetAllSerializations()
        {
            if (_count < 1)
            {
                yield break;
            }

            yield return _cachedItem1;

            if (_count < 2)
            {
                yield break;
            }

            yield return _cachedItem2;

            if (_count < 3)
            {
                yield break;
            }

            foreach (var item in _cachedItems)
            {
                yield return item;
            }
        }

        private void SetCache(string protocolName, ReadOnlyMemory<byte> serialized)
        {
            // We set the fields before moving on to the list, if we need it to hold more than 2 items.
            // In order to prevent "shearing" (where some of the fields of the struct are set by one thread,
            // while another thread is reading the struct), we have a counter that tracks how many items
            // are present. It's only ever modified in the lock, so it doesn't need Interlocked.

            if (_cachedItem1.ProtocolName == null)
            {
                _cachedItem1 = new SerializedMessage(protocolName, serialized);
                _count += 1;
            }
            else if (_cachedItem2.ProtocolName == null)
            {
                _cachedItem2 = new SerializedMessage(protocolName, serialized);
                _count += 1;
            }
            else
            {
                if (_cachedItems == null)
                {
                    _cachedItems = new List<SerializedMessage>();
                    _count += 1;
                }

                // No need to continue updating _count. It's just used to track the fields. The list
                // always has to be accessed under a lock (except in the constructor) so we don't need the counter.

                foreach (var item in _cachedItems)
                {
                    if (string.Equals(item.ProtocolName, protocolName, StringComparison.Ordinal))
                    {
                        // No need to add
                        return;
                    }
                }

                _cachedItems.Add(new SerializedMessage(protocolName, serialized));
            }
        }

        private bool TryGetCachedFast(string protocolName, out ReadOnlyMemory<byte> result)
        {
            if (_count > 0 && string.Equals(_cachedItem1.ProtocolName, protocolName, StringComparison.Ordinal))
            {
                result = _cachedItem1.Serialized;
                return true;
            }

            if (_count > 1 && string.Equals(_cachedItem2.ProtocolName, protocolName, StringComparison.Ordinal))
            {
                result = _cachedItem2.Serialized;
                return true;
            }

            result = ReadOnlyMemory<byte>.Empty;
            return false;
        }

        private bool TryGetCached(string protocolName, out ReadOnlyMemory<byte> result)
        {
            if (TryGetCachedFast(protocolName, out result))
            {
                return true;
            }

            if (_count > 2)
            {
                foreach (var serializedMessage in _cachedItems)
                {
                    if (string.Equals(serializedMessage.ProtocolName, protocolName, StringComparison.Ordinal))
                    {
                        result = serializedMessage.Serialized;
                        return true;
                    }
                }
            }

            result = default;
            return false;
        }
    }
}
