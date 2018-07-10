// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.AspNetCore.SignalR.Protocol
{
    public class StreamPlaceholder
    {
        public string StreamId { get; private set; }

        public StreamPlaceholder(string streamId)
        {
            StreamId = streamId;
        }
    }
}
