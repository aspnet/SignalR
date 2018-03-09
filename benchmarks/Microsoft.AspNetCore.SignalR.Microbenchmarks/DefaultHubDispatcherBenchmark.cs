using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Protocols;
using Microsoft.AspNetCore.SignalR.Internal;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.AspNetCore.SignalR.Microbenchmarks
{
    public class DefaultHubDispatcherBenchmark
    {
        private readonly DefaultHubDispatcher<TestHub> _dispatcher;
        private readonly HubConnectionContext _connectionContext;

        public class TestHub : Hub
        {
            public void Invocation()
            {
            }

            public async Task InvocationAsync()
            {
                await Task.Yield();
            }
        }

        public class TestServiceScopeFactory : IServiceScopeFactory
        {
            public IServiceScope CreateScope()
            {
                return null;
            }
        }

        public DefaultHubDispatcherBenchmark()
        {
            _dispatcher = new DefaultHubDispatcher<TestHub>(
                new TestServiceScopeFactory(),
                new HubContext<TestHub>(new DefaultHubLifetimeManager<TestHub>()),
                new Logger<DefaultHubDispatcher<TestHub>>(NullLoggerFactory.Instance));

            _connectionContext = new HubConnectionContext(new DefaultConnectionContext(new FeatureCollection()), TimeSpan.Zero, NullLoggerFactory.Instance);
        }

        [Benchmark]
        public Task InvocationMessageTest()
        {
            return _dispatcher.DispatchMessageAsync(_connectionContext, new InvocationMessage("123", "Invocation", null));
        }

        [Benchmark]
        public Task InvocationMessageAsyncTest()
        {
            return _dispatcher.DispatchMessageAsync(_connectionContext, new InvocationMessage("123", "InvocationAsync", null));
        }
    }
}
