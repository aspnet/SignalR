// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;

namespace Microsoft.AspNetCore.SignalR.Internal
{
    internal class SyntheticArgumentsManager
    {
        private CancellationTokenSource _cts;
        private readonly HubConnectionContext _connection;

        public SyntheticArgumentsManager(HubConnectionContext connection)
        {
            _connection = connection;
        }

        public bool TryGetSyntheticArgument(Type type, out object argument)
        {
            if (type == typeof(CancellationToken))
            {
                if (_cts == null)
                {
                    _cts = CancellationTokenSource.CreateLinkedTokenSource(_connection.ConnectionAborted);
                }
                argument = _cts.Token;
                return true;
            }

            argument = null;
            return false;
        }

        // May be used for canceling hub invocations, so we need to expose the cts
        public bool TryGetCancellationTokenSource(out CancellationTokenSource cts)
        {
            cts = _cts;
            if (_cts == null)
            {
                return false;
            }

            return true;
        }
    }
}
