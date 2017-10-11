// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.AspNetCore.SignalR
{
    public class HubGroupList : IReadOnlyCollection<ConcurrentDictionary<string, HubConnectionContext>>
    {
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, HubConnectionContext>> _groups =
            new ConcurrentDictionary<string, ConcurrentDictionary<string, HubConnectionContext>>();

        public ConcurrentDictionary<string, HubConnectionContext> this[string groupName]
        {
            get
            {
                if (_groups.TryGetValue(groupName, out var group))
                {
                    return group;
                }
                return null;
            }
        }

        public void Add(HubConnectionContext connection, string groupName)
        {
            CreateOrUpdateGroupWithConnection(groupName, connection);
        }

        public void Remove(string connectionId, string groupName)
        {
            if (_groups.TryGetValue(groupName, out var connections))
            {
                if (connections.TryRemove(connectionId, out var _) && connections.Count == 0)
                {
                    if (_groups.TryRemove(groupName, out var newlyAddedConnections) && newlyAddedConnections != null)
                    {
                        foreach (var c in newlyAddedConnections)
                        {
                            CreateOrUpdateGroupWithConnection(groupName, c.Value);
                        }
                    }
                }
            }
        }

        public int Count => _groups.Count;

        public IEnumerator<ConcurrentDictionary<string, HubConnectionContext>> GetEnumerator()
        {
            foreach (var item in _groups)
            {
                yield return item.Value;
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        private void CreateOrUpdateGroupWithConnection(string groupName, HubConnectionContext connection)
        {
            _groups.AddOrUpdate(groupName, AddConnectionToGroup(connection, new ConcurrentDictionary<string, HubConnectionContext>()), (key, oldCollection) =>
            {
                AddConnectionToGroup(connection, oldCollection);
                return oldCollection;
            });
        }

        private ConcurrentDictionary<string, HubConnectionContext> AddConnectionToGroup(HubConnectionContext connection, ConcurrentDictionary<string, HubConnectionContext> group)
        {
            group.AddOrUpdate(connection.ConnectionId, connection, (key, oldValue) => connection);
            return group;
        }
    }
}
