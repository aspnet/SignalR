// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;

namespace Microsoft.AspNetCore.SignalR.Internal.Protocol
{
    public abstract class HubInvocationMessage : HubMessage
    {
        public string InvocationId { get; }

        protected HubInvocationMessage(IReadOnlyDictionary<string, string> headers, string invocationId)
            : base(headers)
        {
            InvocationId = invocationId;
        }
    }
}
