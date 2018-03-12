// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.AspNetCore.Sockets;

namespace Microsoft.AspNetCore.SignalR.Internal.Protocol
{
    public interface IHubProtocol
    {
        string Name { get; }

        TransferFormat TransferFormat { get; }

        bool TryParseMessages(ReadOnlySpan<byte> input, IInvocationBinder binder, IList<HubMessage> messages);

        void WriteMessage(HubMessage message, Stream output);
    }
}
