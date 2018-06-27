// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.SignalR.Internal;

namespace Microsoft.AspNetCore.SignalR.Internal
{
    internal class HubConnectionBinder<THub> : IInvocationBinder where THub : Hub
    {
        private HubDispatcher<THub> _dispatcher;
        private HubConnectionContext _connection;

        public HubConnectionBinder(HubDispatcher<THub> dispatcher, HubConnectionContext connection)
        {
            _dispatcher = dispatcher;
            _connection = connection;
        }

        public IReadOnlyList<Type> GetParameterTypes(string methodName)
        {
            throw new NotImplementedException();
        }

        public Type GetReturnType(string invocationId)
        {
            throw new NotImplementedException();
        }

        public Type GetStreamItemType(string channelId)
        {
            throw new NotImplementedException();
        }
    }
}