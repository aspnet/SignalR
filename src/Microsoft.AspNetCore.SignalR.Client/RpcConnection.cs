﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Sockets.Client;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.SignalR.Client
{
    public class HubConnection : IDisposable
    {
        private readonly Task _reader;
        private readonly Stream _stream;
        private readonly ILogger _logger;
        private readonly Connection _connection;
        private readonly IInvocationAdapter _adapter;
        private readonly HubBinder _binder;

        private readonly CancellationTokenSource _readerCts = new CancellationTokenSource();
        private readonly ConcurrentDictionary<string, InvocationRequest> _pendingCalls = new ConcurrentDictionary<string, InvocationRequest>();
        private readonly ConcurrentDictionary<string, InvocationHandler> _handlers = new ConcurrentDictionary<string, InvocationHandler>();

        private int _nextId = 0;

        private HubConnection(Connection connection, IInvocationAdapter adapter, ILogger logger)
        {
            _binder = new HubBinder(this);
            _connection = connection;
            _stream = connection.GetStream();
            _adapter = adapter;
            _logger = logger;

            _reader = ReceiveMessages(_readerCts.Token);
        }

        // TODO: Client return values/tasks?
        // TODO: Overloads that use type parameters (like On<T1>, On<T1, T2>, etc.)
        public void On(string methodName, Type[] parameterTypes, Action<object[]> handler)
        {
            var invocationHandler = new InvocationHandler(parameterTypes, handler);
            _handlers.AddOrUpdate(methodName, invocationHandler, (_, __) => invocationHandler);
        }

        public Task<T> Invoke<T>(string methodName, params object[] args) => Invoke<T>(methodName, CancellationToken.None, args);
        public async Task<T> Invoke<T>(string methodName, CancellationToken cancellationToken, params object[] args) => ((T)(await Invoke(methodName, typeof(T), cancellationToken, args)));

        public Task<object> Invoke(string methodName, Type returnType, params object[] args) => Invoke(methodName, returnType, CancellationToken.None, args);
        public async Task<object> Invoke(string methodName, Type returnType, CancellationToken cancellationToken, params object[] args)
        {
            _logger.LogTrace("Preparing invocation of '{0}', with return type '{1}' and {2} args", methodName, returnType.AssemblyQualifiedName, args.Length);

            // Create an invocation descriptor.
            var descriptor = new InvocationDescriptor
            {
                Id = GetNextId(),
                Method = methodName,
                Arguments = args
            };

            // I just want an excuse to use 'irq' as a variable name...
            _logger.LogDebug("Registering Invocation ID '{0}' for tracking", descriptor.Id);
            var irq = new InvocationRequest(cancellationToken, returnType);
            var addedSuccessfully = _pendingCalls.TryAdd(descriptor.Id, irq);

            // This should always be true since we monotonically increase ids.
            Debug.Assert(addedSuccessfully, "Id already in use?");

            // Trace the invocation, but only if that logging level is enabled (because building the args list is a bit slow)
            if (_logger.IsEnabled(LogLevel.Trace))
            {
                var argsList = string.Join(", ", args.Select(a => a.GetType().FullName));
                _logger.LogTrace("Invocation #{0}: {1} {2}({3})", descriptor.Id, returnType.FullName, methodName, argsList);
            }

            // Write the invocation to the stream
            _logger.LogInformation("Sending Invocation #{0}", descriptor.Id);
            await _adapter.WriteMessageAsync(descriptor, _stream, cancellationToken);

            // Return the completion task. It will be completed by ReceiveMessages when the response is received.
            return await irq.Completion.Task;
        }

        public void Dispose()
        {
            _readerCts.Cancel();
            _connection.Dispose();
        }

        // TODO: Clean up the API here. Negotiation of format would be better than providing an adapter instance. Similarly, we should not require a logger factory
        public static Task<HubConnection> ConnectAsync(Uri url, IInvocationAdapter adapter, ITransport transport, PipelineFactory pipelineFactory, ILoggerFactory loggerFactory) => ConnectAsync(url, adapter, transport, new HttpClient(), pipelineFactory, loggerFactory);

        public static async Task<HubConnection> ConnectAsync(Uri url, IInvocationAdapter adapter, ITransport transport, HttpClient httpClient, PipelineFactory pipelineFactory, ILoggerFactory loggerFactory)
        {
            // Connect the underlying connection
            var connection = await Connection.ConnectAsync(url, transport, httpClient, pipelineFactory, loggerFactory);

            // Create the RPC connection wrapper
            return new HubConnection(connection, adapter, loggerFactory.CreateLogger<HubConnection>());
        }

        private async Task ReceiveMessages(CancellationToken cancellationToken)
        {
            await Task.Yield();

            _logger.LogTrace("Beginning receive loop");
            while (!cancellationToken.IsCancellationRequested)
            {
                // This is a little odd... we want to remove the InvocationRequest once and only once so we pull it out in the callback,
                // and stash it here because we know the callback will have finished before the end of the await.
                var message = await _adapter.ReadMessageAsync(_stream, _binder, cancellationToken);

                var invocationDescriptor = message as InvocationDescriptor;
                if (invocationDescriptor != null)
                {
                    DispatchInvocation(invocationDescriptor, cancellationToken);
                }
                else
                {
                    var invocationResultDescriptor = message as InvocationResultDescriptor;
                    if (invocationResultDescriptor != null)
                    {
                        DispatchInvocationResult(invocationResultDescriptor, cancellationToken);
                    }
                }
            }

            // Cancel all pending calls
            foreach(var call in _pendingCalls.Values)
            {
                call.Completion.TrySetCanceled();
            }
            _pendingCalls.Clear();

            _logger.LogTrace("Ending receive loop");
        }

        private void DispatchInvocation(InvocationDescriptor invocationDescriptor, CancellationToken cancellationToken)
        {
            // Find the handler
            InvocationHandler handler;
            if (!_handlers.TryGetValue(invocationDescriptor.Method, out handler))
            {
                _logger.LogWarning("Failed to find handler for '{0}' method", invocationDescriptor.Method);
            }

            // TODO: Return values
            // TODO: Dispatch to a sync context to ensure we aren't blocking this loop.
            handler.Handler(invocationDescriptor.Arguments);
        }

        private void DispatchInvocationResult(InvocationResultDescriptor result, CancellationToken cancellationToken)
        {
            InvocationRequest irq;
            var successfullyRemoved = _pendingCalls.TryRemove(result.Id, out irq);
            Debug.Assert(successfullyRemoved, $"Invocation request {result.Id} was removed from the pending calls dictionary!");

            _logger.LogInformation("Received Result for Invocation #{0}", result.Id);

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            Debug.Assert(irq.Completion != null, "Didn't properly capture InvocationRequest in callback for ReadInvocationResultDescriptorAsync");

            // If the invocation hasn't been cancelled, dispatch the result
            if (!irq.CancellationToken.IsCancellationRequested)
            {
                irq.Registration.Dispose();

                // Complete the request based on the result
                // TODO: the TrySetXYZ methods will cause continuations attached to the Task to run, so we should dispatch to a sync context or thread pool.
                if (!string.IsNullOrEmpty(result.Error))
                {
                    _logger.LogInformation("Completing Invocation #{0} with error: {1}", result.Id, result.Error);
                    irq.Completion.TrySetException(new Exception(result.Error));
                }
                else
                {
                    _logger.LogInformation("Completing Invocation #{0} with result of type: {1}", result.Id, result.Result?.GetType()?.FullName ?? "<<void>>");
                    irq.Completion.TrySetResult(result.Result);
                }
            }
        }

        private string GetNextId() => Interlocked.Increment(ref _nextId).ToString();

        private class HubBinder : IInvocationBinder
        {
            private HubConnection _connection;

            public HubBinder(HubConnection connection)
            {
                _connection = connection;
            }

            public Type GetReturnType(string invocationId)
            {
                InvocationRequest irq;
                if (!_connection._pendingCalls.TryGetValue(invocationId, out irq))
                {
                    _connection._logger.LogError("Unsolicited response received for invocation '{0}'", invocationId);
                    return null;
                }
                return irq.ResultType;
            }

            public Type[] GetParameterTypes(string methodName)
            {
                InvocationHandler handler;
                if (!_connection._handlers.TryGetValue(methodName, out handler))
                {
                    _connection._logger.LogWarning("Failed to find handler for '{0}' method", methodName);
                    return Type.EmptyTypes;
                }
                return handler.ParameterTypes;
            }
        }

        private struct InvocationHandler
        {
            public Action<object[]> Handler { get; }
            public Type[] ParameterTypes { get; }

            public InvocationHandler(Type[] parameterTypes, Action<object[]> handler)
            {
                Handler = handler;
                ParameterTypes = parameterTypes;
            }
        }

        private struct InvocationRequest
        {
            public Type ResultType { get; }
            public CancellationToken CancellationToken { get; }
            public CancellationTokenRegistration Registration { get; }
            public TaskCompletionSource<object> Completion { get; }

            public InvocationRequest(CancellationToken cancellationToken, Type resultType)
            {
                var tcs = new TaskCompletionSource<object>();
                Completion = tcs;
                CancellationToken = cancellationToken;
                Registration = cancellationToken.Register(() => tcs.TrySetCanceled());
                ResultType = resultType;
            }
        }
    }
}
