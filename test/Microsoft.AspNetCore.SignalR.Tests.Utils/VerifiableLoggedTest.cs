// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Xunit.Abstractions;

namespace Microsoft.AspNetCore.SignalR.Tests
{
    public class VerifiableLoggedTest : LoggedTest
    {
        public VerifiableLoggedTest(ITestOutputHelper output = null) : base(output)
        {
        }

        public virtual IDisposable StartVerifiableLog([CallerMemberName] string testName = null, Func<WriteContext, bool> expectedErrorsFilter = null)
        {
            return CreateScope(expectedErrorsFilter);
        }

        public virtual IDisposable StartVerifiableLog(LogLevel minLogLevel, [CallerMemberName] string testName = null, Func<WriteContext, bool> expectedErrorsFilter = null)
        {
            return CreateScope(expectedErrorsFilter);
        }

        private VerifyNoErrorsScope CreateScope(Func<WriteContext, bool> expectedErrorsFilter = null)
        {
            return new VerifyNoErrorsScope(LoggerFactory, wrappedDisposable: null, expectedErrorsFilter);
        }
    }
}
