﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Microsoft.AspNetCore.Sockets
{
    [Flags]
    public enum TransportType
    {
        WebSockets = 1,
        ServerSentEvents = 2,
        LongPolling = 4,
        All = WebSockets | ServerSentEvents | LongPolling
    }
}
