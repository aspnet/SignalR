// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO.Pipelines;
using System.Reactive.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
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
            private static readonly IObservable<int> ObservableInstance = Observable.Empty<int>();
            private static readonly ChannelReader<int> ChannelReaderInstance = Channel.CreateUnbounded<int>();

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

            public IObservable<int> StreamObservable()
            {
                return ObservableInstance;
            }

            public Task<IObservable<int>> StreamObservableAsync()
            {
                return Task.FromResult(ObservableInstance);
            }

            public ValueTask<IObservable<int>> StreamObservableValueTaskAsync()
            {
                return new ValueTask<IObservable<int>>(ObservableInstance);
            }

            public ChannelReader<int> StreamChannelReader()
            {
                return ChannelReaderInstance;
            }

            public Task<ChannelReader<int>> StreamChannelReaderAsync()
            {
                return Task.FromResult(ChannelReaderInstance);
            }

            public ValueTask<ChannelReader<int>> StreamChannelReaderValueTaskAsync()
            {
                return new ValueTask<ChannelReader<int>>(ChannelReaderInstance);
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
        public Task StreamObservable()
        {
            return _dispatcher.DispatchMessageAsync(_connectionContext, new StreamInvocationMessage("123", "StreamObservable", null));
        }

        [Benchmark]
        public Task StreamObservableAsync()
        {
            return _dispatcher.DispatchMessageAsync(_connectionContext, new StreamInvocationMessage("123", "StreamObservableAsync", null));
        }

        [Benchmark]
        public Task StreamObservableValueTaskAsync()
        {
            return _dispatcher.DispatchMessageAsync(_connectionContext, new StreamInvocationMessage("123", "StreamObservableValueTaskAsync", null));
        }

        [Benchmark]
        public Task StreamChannelReader()
        {
            return _dispatcher.DispatchMessageAsync(_connectionContext, new StreamInvocationMessage("123", "StreamObservable", null));
        }

        [Benchmark]
        public Task StreamChannelReaderAsync()
        {
            return _dispatcher.DispatchMessageAsync(_connectionContext, new StreamInvocationMessage("123", "StreamObservableAsync", null));
        }

        [Benchmark]
        public Task StreamChannelReaderValueTaskAsync()
        {
            return _dispatcher.DispatchMessageAsync(_connectionContext, new StreamInvocationMessage("123", "StreamObservableValueTaskAsync", null));
        }
    }
}