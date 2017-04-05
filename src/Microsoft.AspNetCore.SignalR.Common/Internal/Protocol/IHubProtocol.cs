// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.AspNetCore.Sockets;

namespace Microsoft.AspNetCore.SignalR.Internal.Protocol
{
    public interface IHubProtocol
    {
        MessageType MessageType { get; }

        bool TryParseMessage(ReadOnlySpan<byte> input, IInvocationBinder binder, out HubMessage message);

        // Need a better API when we have sorted out pooling, etc.
        // See https://github.com/aspnet/SignalR/issues/126
        // For exmaple: bool TryWriteMessage(HubMessage message, IOutput output);
        byte[] WriteMessage(HubMessage message);
    }
}
