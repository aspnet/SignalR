using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Protocols;
using Microsoft.AspNetCore.SignalR.Internal;
using Microsoft.AspNetCore.SignalR.Internal.Encoders;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using DefaultConnectionContext = Microsoft.AspNetCore.Sockets.DefaultConnectionContext;

namespace Microsoft.AspNetCore.SignalR.Microbenchmarks
{
    public class DefaultHubDispatcherBenchmark
    {
        private DefaultHubDispatcher<TestHub> _dispatcher;
        private HubConnectionContext _connectionContext;

        [GlobalSetup]
        public void GlobalSetup()
        {
            var serviceCollection = new ServiceCollection();
            serviceCollection.AddSignalRCore();

            var provider = serviceCollection.BuildServiceProvider();

            var serviceScopeFactory = provider.GetService<IServiceScopeFactory>();

            _dispatcher = new DefaultHubDispatcher<TestHub>(
                serviceScopeFactory,
                new HubContext<TestHub>(new DefaultHubLifetimeManager<TestHub>()),
                new Logger<DefaultHubDispatcher<TestHub>>(NullLoggerFactory.Instance));

            var options = new PipeOptions();
            var pair = DuplexPipe.CreateConnectionPair(options, options);
            var connection = new DefaultConnectionContext(Guid.NewGuid().ToString(), pair.Transport, pair.Application);

            _connectionContext = new HubConnectionContext(connection, TimeSpan.Zero, NullLoggerFactory.Instance);

            var hubProtocol = Moq.Mock.Of<IHubProtocol>();
            var dataEncoder = Moq.Mock.Of<IDataEncoder>();

            _connectionContext.ProtocolReaderWriter = new HubProtocolReaderWriter(hubProtocol, dataEncoder);
        }

        public class TestHub : Hub
        {
            public void Invocation()
            {
            }

            public async Task InvocationAsync()
            {
                await Task.Yield();
            }

            public int InvocationReturnValue()
            {
                return 1;
            }

            public async Task<int> InvocationReturnAsync()
            {
                await Task.Yield();
                return 1;
            }

            public async ValueTask<int> InvocationValueTaskAsync()
            {
                await Task.Yield();
                return 1;
            }

            public IObservable<int> SteamObservable()
            {
                return Observable.Empty<int>();
            }

            public async Task<IObservable<int>> SteamObservableAsync()
            {
                await Task.Yield();
                return Observable.Empty<int>();
            }

            public async ValueTask<IObservable<int>> SteamObservableValueTaskAsync()
            {
                await Task.Yield();
                return Observable.Empty<int>();
            }
        }

        public class TestServiceScopeFactory : IServiceScopeFactory
        {
            public IServiceScope CreateScope()
            {
                return null;
            }
        }

        [Benchmark]
        public Task Invocation()
        {
            return _dispatcher.DispatchMessageAsync(_connectionContext, new InvocationMessage("123", "Invocation", null));
        }

        [Benchmark]
        public Task InvocationAsync()
        {
            return _dispatcher.DispatchMessageAsync(_connectionContext, new InvocationMessage("123", "InvocationAsync", null));
        }

        [Benchmark]
        public Task InvocationReturnValue()
        {
            return _dispatcher.DispatchMessageAsync(_connectionContext, new InvocationMessage("123", "InvocationReturnValue", null));
        }

        [Benchmark]
        public Task InvocationReturnAsync()
        {
            return _dispatcher.DispatchMessageAsync(_connectionContext, new InvocationMessage("123", "InvocationReturnAsync", null));
        }

        [Benchmark]
        public Task InvocationValueTaskAsync()
        {
            return _dispatcher.DispatchMessageAsync(_connectionContext, new InvocationMessage("123", "InvocationValueTaskAsync", null));
        }

        [Benchmark]
        public Task SteamObservable()
        {
            return _dispatcher.DispatchMessageAsync(_connectionContext, new StreamInvocationMessage("123", "SteamObservable", null));
        }

        [Benchmark]
        public Task SteamObservableAsync()
        {
            return _dispatcher.DispatchMessageAsync(_connectionContext, new StreamInvocationMessage("123", "SteamObservableAsync", null));
        }

        [Benchmark]
        public Task SteamObservableValueTaskAsync()
        {
            return _dispatcher.DispatchMessageAsync(_connectionContext, new StreamInvocationMessage("123", "SteamObservableValueTaskAsync", null));
        }

        //public IObservable<int> Stream(int count) => TestHubMethodsImpl.Stream(count);

        //public ChannelReader<int> StreamException() => TestHubMethodsImpl.StreamException();

        //public ChannelReader<string> StreamBroken() => TestHubMethodsImpl.StreamBroken();

    }
}
