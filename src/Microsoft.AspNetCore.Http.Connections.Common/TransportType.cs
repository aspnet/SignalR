// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Microsoft.AspNetCore.Http.Connections
{
    [Flags]
    public enum HttpTransportType
    {
        WebSockets = 1,
        ServerSentEvents = 2,
        LongPolling = 4,
        All = WebSockets | ServerSentEvents | LongPolling
    }
}
