// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Redis;
using Xunit;

namespace Microsoft.AspNetCore.SignalR.Tools.Tests
{
    public class HubDiscoveryTests
    {
        [Theory]
        [InlineData(null, typeof(ArgumentNullException))]
        [InlineData("???", typeof(ArgumentException))]
        [InlineData("NonExistingDll", typeof(FileNotFoundException))]
        public void HubDiscoveryThrowsForNonExistentDll(string path, Type expectedException)
        {
            Assert.Throws(expectedException, () => new HubDiscovery(path));
        }

        [Theory]
        [MemberData(nameof(AssembliesWithoutHubs))]
        public void HubDiscoveryReturnsEmptyListIfNoHubsDiscovered(Assembly assembly)
        {
            using (var discovery = new HubDiscovery(assembly.Location))
            {
                Assert.Empty(discovery.GetHubProxies());
            }
        }

        public static IEnumerable<object[]> AssembliesWithoutHubs
            => new[]
            {
                // This assembly does not depend on Microsoft.AspNetCore.SignalR
                new object[] { typeof(object).Assembly },
                // Should not discover hub base types
                new object[] { typeof(Hub<>).Assembly },
                // This assembly depends on Microsoft.AspNetCore.SignalR but does not have any hubs
                new object[] { typeof(RedisOptions).Assembly }
            };

        [Fact]
        public void HubDiscoveryDiscoversHubs()
        {
            using (var discovery = new HubDiscovery(GetType().Assembly.Location, t => !t.Namespace.Contains("BadHubs")))
            {
                var proxies = discovery.GetHubProxies();
                Assert.Equal(
                    new[] { typeof(TestHub).FullName, typeof(BaseHub).FullName, typeof(DisposableHub).FullName },
                    proxies.Select(p => $"{p.Namespace}.{p.Name}"));
            }
        }

        [Fact]
        public void HubDiscoveryDiscoversInheritedMethods()
        {
            using (var discovery = new HubDiscovery(GetType().Assembly.Location, t => !t.Namespace.Contains("BadHubs")))
            {
                var proxy = discovery.GetHubProxies().Single(p => p.Name == nameof(TestHub));
                Assert.Contains(proxy.Methods, m => m.Name == "MethodOnBaseHub");
            }
        }

        [Fact]
        public void HubDiscoveryDiscoversOverridenMethods()
        {
            using (var discovery = new HubDiscovery(GetType().Assembly.Location, t => t.Name == nameof(TestHub)))
            {
                var proxy = discovery.GetHubProxies().Single();
                Assert.Contains(proxy.Methods, m => m.Name == "VirtualHubMethod");
            }
        }

        [Fact]
        public void HubDiscoveryIgnoresPrivateAndStaticMethods()
        {
            using (var discovery = new HubDiscovery(GetType().Assembly.Location, t => !t.Namespace.Contains("BadHubs")))
            {
                var proxy = discovery.GetHubProxies().Single(p => p.Name == nameof(BaseHub));
                Assert.DoesNotContain(proxy.Methods, m => m.Name == "PrivateMethod");
                Assert.DoesNotContain(proxy.Methods, m => m.Name == "ProtectedMethod");
                Assert.DoesNotContain(proxy.Methods, m => m.Name == "StaticMethod");
            }
        }

        [Fact]
        public void HubDiscoveryIgnoresMethodsDefinedOnHubOfT()
        {
            using (var discovery = new HubDiscovery(GetType().Assembly.Location, t => t.Name == nameof(BaseHub)))
            {
                var proxy = discovery.GetHubProxies().Single();
                Assert.DoesNotContain(proxy.Methods, m => m.Name == "OnConnectedAsync");
                Assert.DoesNotContain(proxy.Methods, m => m.Name == "OnDisconnectedAsync");
            }
        }

        [Fact]
        public void HubDiscoveryIgnoresMethodsDefinedOIDisposable()
        {
            using (var discovery = new HubDiscovery(GetType().Assembly.Location, t => t.Name == nameof(DisposableHub)))
            {
                Assert.Empty(discovery.GetHubProxies().Single().Methods);
            }
        }

        [Theory]
        [InlineData(nameof(BadHubs.HubWithOverloads), "OverloadedMethod")]
        [InlineData(nameof(BadHubs.HubWithCaseInsensitiveOverloads), "OverloadedMETHOD")]
        public void HubDiscoveryThrowsForOverloadedMethods(string hubName, string methodName)
        {
            using (var discovery = new HubDiscovery(GetType().Assembly.Location, t => t.Name == hubName))
            {
                var ex = Assert.Throws<InvalidOperationException>(() => discovery.GetHubProxies());
                Assert.Equal($"Duplicate definitions of '{methodName}'. Overloading is not supported.", ex.Message);
            }
        }
    }

    public class TestHub : BaseHub
    {
        public override void VirtualHubMethod()
        {
            base.VirtualHubMethod();
        }
    }

    public class BaseHub : Hub
    {
        public override Task OnConnectedAsync()
        {
            return base.OnConnectedAsync();
        }

        public override Task OnDisconnectedAsync(Exception exception)
        {
            return base.OnDisconnectedAsync(exception);
        }

        public Task<int> MethodOnBaseHub()
        {
            return Task.FromResult(42);
        }

        public virtual void VirtualHubMethod()
        {
        }

        private void PrivateMethod() { }

        protected void ProtectedMethod() { }

        public static void StaticMethod() { }
    }

    public class DisposableHub : Hub, IDisposable
    {
        void IDisposable.Dispose() { }
    }

    namespace BadHubs
    {
        public class HubWithOverloads : Hub
        {
            public void OverloadedMethod() { }

            public void OverloadedMethod(string parameter) { }
        }

        public class BaseHubWithCaseInsensitiveOverloads : Hub
        {
            public void OverloadedMETHOD() { }
        }

        public class HubWithCaseInsensitiveOverloads : BaseHubWithCaseInsensitiveOverloads
        {
            public void OverloadedMethod(string parameter) { }
        }
    }
}
