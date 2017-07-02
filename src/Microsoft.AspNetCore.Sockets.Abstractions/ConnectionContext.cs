// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO.Pipelines;
using Microsoft.AspNetCore.Http.Features;

namespace Microsoft.AspNetCore.Sockets
{
    public abstract class ConnectionContext
    {
        public abstract string ConnectionId { get; }

        public abstract IFeatureCollection Features { get; }

        // REVIEW: Should this be changed to items
        public abstract ConnectionMetadata Metadata { get; }

        public abstract PipeFactory PipeFactory { get; }

        public abstract IPipe Transport { get; set; }
    }
}
