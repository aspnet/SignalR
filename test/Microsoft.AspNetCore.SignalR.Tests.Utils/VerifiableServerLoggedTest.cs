// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Xunit.Abstractions;

namespace Microsoft.AspNetCore.SignalR.Tests
{
    public class VerifiableServerLoggedTest : VerifiableLoggedTest
    {
        private readonly Func<WriteContext, bool> _globalExpectedErrorsFilter;

        public ServerFixture ServerFixture { get; }

        public VerifiableServerLoggedTest(ServerFixture serverFixture, ITestOutputHelper output) : base(output)
        {
            ServerFixture = serverFixture;

            // Suppress errors globally here
            _globalExpectedErrorsFilter = (writeContext) => false;
        }

        private Func<WriteContext, bool> ResolveExpectedErrorsFilter(Func<WriteContext, bool> expectedErrorsFilter)
        {
            if (expectedErrorsFilter == null)
            {
                return _globalExpectedErrorsFilter;
            }

            return (writeContext) =>
            {
                if (expectedErrorsFilter(writeContext))
                {
                    return true;
                }

                return _globalExpectedErrorsFilter(writeContext);
            };
        }

        public IDisposable StartVerifiableServerLog<T>(out ILoggerFactory loggerFactory, out ServerFixture<T> serverFixture, LogLevel minLogLevel, [CallerMemberName] string testName = null, Func<WriteContext, bool> expectedErrorsFilter = null) where T : class
        {
            var disposable = base.StartVerifiableLog(out loggerFactory, minLogLevel, testName, ResolveExpectedErrorsFilter(expectedErrorsFilter));
            serverFixture = new ServerFixture<T>(loggerFactory);
            return new MultiDisposable(serverFixture, disposable);
        }

        public IDisposable StartVerifiableServerLog<T>(out ILoggerFactory loggerFactory, out ServerFixture<T> serverFixture, [CallerMemberName] string testName = null, Func<WriteContext, bool> expectedErrorsFilter = null) where T : class
        {
            var disposable = base.StartVerifiableLog(out loggerFactory, testName, ResolveExpectedErrorsFilter(expectedErrorsFilter));
            serverFixture = new ServerFixture<T>(loggerFactory);
            return new MultiDisposable(serverFixture, disposable);
        }

        public override IDisposable StartVerifiableLog(out ILoggerFactory loggerFactory, LogLevel minLogLevel, [CallerMemberName] string testName = null, Func<WriteContext, bool> expectedErrorsFilter = null)
        {
            var disposable = base.StartVerifiableLog(out loggerFactory, minLogLevel, testName, ResolveExpectedErrorsFilter(expectedErrorsFilter));
            return new ServerLogScope(ServerFixture, loggerFactory, disposable);
        }

        public override IDisposable StartVerifiableLog(out ILoggerFactory loggerFactory, [CallerMemberName] string testName = null, Func<WriteContext, bool> expectedErrorsFilter = null)
        {
            var disposable = base.StartVerifiableLog(out loggerFactory, testName, ResolveExpectedErrorsFilter(expectedErrorsFilter));
            return new ServerLogScope(ServerFixture, loggerFactory, disposable);
        }

        public override void Dispose()
        {
            // Unit tests in a fixture reuse the server.
            // A small delay prevents server logging from a previous tests from showing up in the next test's logs
            // by giving the server time to finish any in-progress request logic.
            Thread.Sleep(TimeSpan.FromMilliseconds(100));
            base.Dispose();
        }

        private class MultiDisposable : IDisposable
        {
            List<IDisposable> _disposables;
            public MultiDisposable(params IDisposable[] disposables)
            {
                _disposables = new List<IDisposable>(disposables);
            }

            public void Dispose()
            {
                foreach (var disposable in _disposables)
                {
                    disposable.Dispose();
                }
            }
        }
    }
}