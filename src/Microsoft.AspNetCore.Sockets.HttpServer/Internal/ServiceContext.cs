// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Microsoft.AspNetCore.Sockets.HttpServer
{
    public class ServiceContext
    {
        public IKestrelTrace Log { get; set; }

        public IThreadPool ThreadPool { get; set; }

        public Func<FrameAdapter, IHttpParser<FrameAdapter>> HttpParserFactory { get; set; }

        public ISystemClock SystemClock { get; set; }

        public DateHeaderValueManager DateHeaderValueManager { get; set; }

        public FrameConnectionManager ConnectionManager { get; set; }
    }
}
