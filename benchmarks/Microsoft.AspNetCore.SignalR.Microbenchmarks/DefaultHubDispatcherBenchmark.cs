using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Attributes.Jobs;
using BenchmarkDotNet.Engines;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Protocols;
using Microsoft.AspNetCore.SignalR.Internal;
using Microsoft.AspNetCore.SignalR.Internal.Encoders;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using DefaultConnectionContext = Microsoft.AspNetCore.Sockets.DefaultConnectionContext;

namespace Microsoft.AspNetCore.SignalR.Microbenchmarks
{
    //[SimpleJob(RunStrategy.Throughput, launchCount: 1, invocationCount: 10)]
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

            _connectionContext = new NoErrorHubConnectionContext(connection, TimeSpan.Zero, NullLoggerFactory.Instance);

            var hubProtocolMock = new Mock<IHubProtocol>();
            var dataEncoder = Mock.Of<IDataEncoder>();

            _connectionContext.ProtocolReaderWriter = new HubProtocolReaderWriter(hubProtocolMock.Object, dataEncoder);
        }

        public class NoErrorHubConnectionContext : HubConnectionContext
        {
            public NoErrorHubConnectionContext(ConnectionContext connectionContext, TimeSpan keepAliveInterval, ILoggerFactory loggerFactory) : base(connectionContext, keepAliveInterval, loggerFactory)
            {
            }

            public override Task WriteAsync(HubMessage message)
            {
                if (message is CompletionMessage completionMessage)
                {
                    if (!string.IsNullOrEmpty(completionMessage.Error))
                    {
                        throw new Exception("Error invoking hub method: " + completionMessage.Error);
                    }
                }

                return Task.CompletedTask;
            }
        }

        public class TestHub : Hub
        {
            public void Invocation()
            {
            }

            public Task InvocationAsync()
            {
                return Task.CompletedTask;
            }

            public int InvocationReturnValue()
            {
                return 1;
            }

            public Task<int> InvocationReturnAsync()
            {
                return Task.FromResult(1);
            }

            public ValueTask<int> InvocationValueTaskAsync()
            {
                return new ValueTask<int>(1);
            }

            public IObservable<int> SteamObservable()
            {
                return Observable.Empty<int>();
            }

            public Task<IObservable<int>> SteamObservableAsync()
            {
                return Task.FromResult(Observable.Empty<int>());
            }

            public ValueTask<IObservable<int>> SteamObservableValueTaskAsync()
            {
                return new ValueTask<IObservable<int>>(Observable.Empty<int>());
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
