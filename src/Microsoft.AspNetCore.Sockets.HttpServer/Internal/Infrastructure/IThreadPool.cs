// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.IO.Pipelines;

namespace Microsoft.AspNetCore.Sockets.HttpServer
{
    public interface IThreadPool : IScheduler
    {
        void Run(Action action);
        void UnsafeRun(WaitCallback action, object state);
    }
}