// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace Microsoft.AspNetCore.SignalR.Tests
{
    internal class TestSinkLoggerProvider : ILoggerProvider
    {
        private TestSink _sink;

        public TestSinkLoggerProvider(TestSink sink)
        {
            _sink = sink;
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new TestLogger(categoryName, _sink, enabled: true);
        }

        public void Dispose()
        {
        }
    }
}
