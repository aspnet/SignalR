// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;

namespace Microsoft.AspNetCore.SignalR.Internal.Protocol
{
    public interface IHubProtocol
    {
        string Name { get; }

        bool TryParseMessages(ReadableBuffer input, IInvocationBinder binder, out ReadCursor consumed, out ReadCursor examined, out IList<HubMessage> messages);

        void WriteMessage(HubMessage message, Stream output);
    }
}
