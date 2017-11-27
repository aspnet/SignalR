// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Authorization;

namespace Microsoft.AspNetCore.Sockets
{
    public class HttpSocketOptions
    {
        public static readonly TimeSpan DefaultKeepAliveInterval = TimeSpan.FromSeconds(30);

        public IList<IAuthorizeData> AuthorizationData { get; } = new List<IAuthorizeData>();

        public TransportType Transports { get; set; } = TransportType.All;

        public WebSocketOptions WebSockets { get; } = new WebSocketOptions();

        public LongPollingOptions LongPolling { get; } = new LongPollingOptions();

        /// <summary>
        /// The interval at which keep-alive messages should be sent. This will be 
        /// </summary>
        public TimeSpan KeepAliveInterval { get; set; } = DefaultKeepAliveInterval;
    }
}
