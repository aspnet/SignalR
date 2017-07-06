﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Moq;
using Moq.Protected;
using Xunit;

namespace Microsoft.AspNetCore.SignalR.Tests
{
    public class DefaultHubActivatorTests
    {
        [Fact]
        public void HubCreatedIfNotResolvedFromServiceProvider()
        {
            Assert.NotNull(
                new DefaultHubActivator<Hub, object>(Mock.Of<IServiceProvider>()).Create());
        }

        [Fact]
        public void HubCanBeResolvedFromServiceProvider()
        {
            var hub = Mock.Of<Hub>();
            var mockServiceProvider = new Mock<IServiceProvider>();
            mockServiceProvider
                .Setup(sp => sp.GetService(typeof(Hub)))
                .Returns(hub);

            Assert.Same(hub,
                new DefaultHubActivator<Hub, object>(mockServiceProvider.Object).Create());
        }


        [Fact]
        public void DisposeNotCalledForHubsResolvedFromServiceProvider()
        {
            var mockServiceProvider = new Mock<IServiceProvider>();
            mockServiceProvider
                .Setup(sp => sp.GetService(typeof(Hub)))
                .Returns(() =>
                {
                    var m = new Mock<Hub>();
                    m.Protected().Setup("Dispose", ItExpr.IsAny<bool>());
                    return m.Object;
                });

            var hubActivator = new DefaultHubActivator<Hub, object>(mockServiceProvider.Object);
            var hub = hubActivator.Create();
            hubActivator.Release(hub);
            Mock.Get(hub).Protected().Verify("Dispose", Times.Never(), ItExpr.IsAny<bool>());
        }

        [Fact]
        public void CannotReleaseNullHub()
        {
            Assert.Equal("hub",
                Assert.Throws<ArgumentNullException>(
                    () => new DefaultHubActivator<Hub, object>(Mock.Of<IServiceProvider>()).Release(null)).ParamName);
        }
    }
}
