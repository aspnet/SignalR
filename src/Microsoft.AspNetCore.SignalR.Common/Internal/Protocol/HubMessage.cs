// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;

namespace Microsoft.AspNetCore.SignalR.Internal.Protocol
{
    public abstract class HubMessage
    {
        public IDictionary<string, string> Headers { get; set; }

        protected HubMessage()
        {
            Headers = new Dictionary<string, string>();
        }
    }
}
