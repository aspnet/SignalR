// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.IO.Pipelines;

namespace Microsoft.AspNetCore.SignalR.Microbenchmarks.Shared
{
    public class TestDuplexPipe : IDuplexPipe
    {
        public PipeReader Input { get; }
        public PipeWriter Output { get; }

        public TestDuplexPipe(ReadResult readResult = default)
        {
            Input = new TestPipeReader(readResult);
            Output = new TestPipeWriter();
        }
    }
}