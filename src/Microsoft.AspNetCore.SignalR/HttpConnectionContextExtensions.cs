﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Sockets.Http.Features;

namespace Microsoft.AspNetCore.SignalR
{
    public static class HttpConnectionContextExtensions
    {
        public static HttpContext GetHttpContext(this HubConnectionContext connection)
        {
            return connection.Features.Get<IHttpContextFeature>()?.HttpContext;
        }
    }
}
