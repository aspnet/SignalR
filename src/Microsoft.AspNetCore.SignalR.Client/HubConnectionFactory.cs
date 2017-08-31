// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Microsoft.AspNetCore.Sockets;
using Microsoft.AspNetCore.Sockets.Client;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.SignalR.Client
{
    public static class HubConnectionFactory
    {
        public static HubConnection Create(Uri url, ILoggerFactory loggerFactory = null)
        {
            var httpConnection = new HttpConnection(url, loggerFactory);
            return new HubConnection(httpConnection, loggerFactory);
        }

        public static HubConnection Create(Uri url, TransportType transportType, ILoggerFactory loggerFactory = null)
        {
            var httpConnection = new HttpConnection(url, transportType, loggerFactory);
            return new HubConnection(httpConnection, loggerFactory);
        }

        public static HubConnection Create(Uri url, TransportType transportType, IHubProtocol protocol, ILoggerFactory loggerFactory = null)
        {
            var httpConnection = new HttpConnection(url, transportType, loggerFactory);
            return new HubConnection(httpConnection, protocol, loggerFactory);
        }
    }
}
