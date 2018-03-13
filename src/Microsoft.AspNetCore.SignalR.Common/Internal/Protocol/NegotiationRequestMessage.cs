// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Microsoft.AspNetCore.SignalR.Internal.Protocol
{
    public class NegotiationRequestMessage : HubMessage
    {
        public NegotiationRequestMessage(string protocol)
        {
            Protocol = protocol;
        }

        public string Protocol { get; }
    }
}
