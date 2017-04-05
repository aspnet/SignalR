// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Internal;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Microsoft.AspNetCore.Sockets;
using Microsoft.AspNetCore.Sockets.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;

namespace Microsoft.AspNetCore.SignalR.Client
{
    public class HubConnection
    {
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger _logger;
        private readonly IConnection _connection;
        private readonly IHubProtocol _protocol;
        private readonly HubBinder _binder;

        private HttpClient _httpClient;

        private readonly CancellationTokenSource _connectionActive = new CancellationTokenSource();

        private readonly object _pendingCallsLock = new object();
        private readonly Dictionary<string, InvocationRequest> _pendingCalls = new Dictionary<string, InvocationRequest>();
        private readonly ConcurrentDictionary<string, InvocationHandler> _handlers = new ConcurrentDictionary<string, InvocationHandler>();

        private int _nextId = 0;

        public event Action Connected
        {
            add { _connection.Connected += value; }
            remove { _connection.Connected -= value; }
        }

        public event Action<Exception> Closed
        {
            add { _connection.Closed += value; }
            remove { _connection.Closed -= value; }
        }

        public HubConnection(Uri url)
            : this(new Connection(url), new JsonHubProtocol(new JsonSerializer()), null)
        { }

        public HubConnection(Uri url, ILoggerFactory loggerFactory)
            : this(new Connection(url), new JsonHubProtocol(new JsonSerializer()), loggerFactory)
        { }

        // These are only really needed for tests now...
        public HubConnection(IConnection connection, ILoggerFactory loggerFactory)
            : this(connection, new JsonHubProtocol(new JsonSerializer()), loggerFactory)
        { }

        public HubConnection(IConnection connection, IHubProtocol protocol, ILoggerFactory loggerFactory)
        {
            if (connection == null)
            {
                throw new ArgumentNullException(nameof(connection));
            }

            _connection = connection;
            _binder = new HubBinder(this);
            _protocol = protocol;
            _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
            _logger = _loggerFactory.CreateLogger<HubConnection>();
            _connection.Received += OnDataReceived;
            _connection.Closed += Shutdown;
        }

        public Task StartAsync() => StartAsync(TransportType.All, httpClient: null);
        public Task StartAsync(HttpClient httpClient) => StartAsync(TransportType.All, httpClient: httpClient);
        public Task StartAsync(TransportType transportType) => StartAsync(transportType, httpClient: null);

        public async Task StartAsync(TransportType transportType, HttpClient httpClient)
        {
            if (httpClient == null)
            {
                // We are creating the client so store it to be able to dispose
                _httpClient = httpClient = new HttpClient();
            }

            await _connection.StartAsync(new DefaultTransportFactory(transportType, _loggerFactory, httpClient), httpClient);
        }

        public async Task StartAsync(ITransportFactory transportFactory, HttpClient httpClient)
        {
            await _connection.StartAsync(transportFactory, httpClient);
        }

        public async Task DisposeAsync()
        {
            await _connection.DisposeAsync();
            _httpClient?.Dispose();
        }

        // TODO: Client return values/tasks?
        // TODO: Overloads for void hub methods
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
            if (_connectionActive.Token.IsCancellationRequested)
            {
                throw new InvalidOperationException("Connection has been terminated.");
            }

            _logger.LogTrace("Preparing invocation of '{0}', with return type '{1}' and {2} args", methodName, returnType.AssemblyQualifiedName, args.Length);

            // Create an invocation descriptor. Client invocations are always blocking
            var invocationMessage = new InvocationMessage(GetNextId().ToString(), methodName, args, nonBlocking: false);

            // I just want an excuse to use 'irq' as a variable name...
            _logger.LogDebug("Registering Invocation ID '{0}' for tracking", invocationMessage.InvocationId);
            var irq = new InvocationRequest(cancellationToken, returnType);

            EnqueueRequest(invocationMessage.InvocationId, irq);

            // Trace the full invocation, but only if that logging level is enabled (because building the args list is a bit slow)
            if (_logger.IsEnabled(LogLevel.Trace))
            {
                var argsList = string.Join(", ", args.Select(a => a.GetType().FullName));
                _logger.LogTrace("Issuing Invocation '{invocationId}': {returnType} {methodName}({args})", invocationMessage.InvocationId, returnType.FullName, methodName, argsList);
            }

            try
            {
                var payload = await _protocol.WriteToArrayAsync(invocationMessage);

                _logger.LogInformation("Sending Invocation '{invocationId}'", invocationMessage.InvocationId);

                await _connection.SendAsync(payload, _protocol.MessageType, cancellationToken);
                _logger.LogInformation("Sending Invocation '{invocationId}' complete", invocationMessage.InvocationId);
            }
            catch (Exception ex)
            {
                _logger.LogError(0, ex, "Sending Invocation '{invocationId}' failed", invocationMessage.InvocationId);
                irq.Complete(ex);
                RemoveRequest(invocationMessage.InvocationId);
            }

            // Return the completion task. It will be completed by ReceiveMessages when the response is received.
            return await irq.Completion;
        }

        private void OnDataReceived(byte[] data, MessageType messageType)
        {
            if (!_protocol.TryParseMessage(data, _binder, out var message))
            {
                _logger.LogError("Received invalid message");
                return;
            }

            InvocationRequest irq;
            switch (message)
            {
                case InvocationMessage invocation:
                    if (_logger.IsEnabled(LogLevel.Trace))
                    {
                        var argsList = string.Join(", ", invocation.Arguments.Select(a => a.GetType().FullName));
                        _logger.LogTrace("Received Invocation '{invocationId}': {methodName}({args})", invocation.InvocationId, invocation.Target, argsList);
                    }
                    DispatchInvocation(invocation, _connectionActive.Token);
                    break;
                case CompletionMessage completion:
                    lock (_pendingCallsLock)
                    {
                        if (!_pendingCalls.TryGetValue(completion.InvocationId, out irq))
                        {
                            _logger.LogWarning("Dropped unsolicited Completion message for invocation '{invocationId}'", completion.InvocationId);
                            return;
                        }
                    }
                    DispatchInvocationCompletion(completion, irq, _connectionActive.Token);
                    RemoveRequest(completion.InvocationId);
                    break;
                case StreamItemMessage streamItem:
                    // Complete the invocation with an error, we don't support streaming (yet)
                    lock (_pendingCallsLock)
                    {
                        if (!_pendingCalls.TryGetValue(streamItem.InvocationId, out irq))
                        {
                            _logger.LogWarning("Dropped unsolicited Completion message for invocation '{invocationId}'", streamItem.InvocationId);
                            return;
                        }
                    }
                    irq.Complete(new NotSupportedException("Streaming method results are not supported"));
                    break;
                default:
                    throw new InvalidOperationException($"Unknown message type: {message.GetType().FullName}");
            }
        }

        private void Shutdown(Exception ex = null)
        {
            _logger.LogTrace("Shutting down connection");
            if (ex != null)
            {
                _logger.LogError("Connection is shutting down due to an error: {0}", ex);
            }

            _connectionActive.Cancel();

            lock (_pendingCallsLock)
            {
                foreach (var call in _pendingCalls.Values)
                {
                    if (ex != null)
                    {
                        call.Complete(ex);
                    }

                    // This will cancel the completion if nobody has done so already.
                    call.Dispose();
                }
                _pendingCalls.Clear();
            }
        }

        private void DispatchInvocation(InvocationMessage invocation, CancellationToken cancellationToken)
        {
            // Find the handler
            if (!_handlers.TryGetValue(invocation.Target, out InvocationHandler handler))
            {
                _logger.LogWarning("Failed to find handler for '{0}' method", invocation.Target);
                return;
            }

            // TODO: Return values
            // TODO: Dispatch to a sync context to ensure we aren't blocking this loop.
            handler.Handler(invocation.Arguments);
        }

        private void DispatchInvocationResult(StreamItemMessage result, InvocationRequest irq, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Received Result for Invocation #{0}", result.InvocationId);

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            // If the invocation hasn't been cancelled, dispatch the result
            if (!irq.CancellationToken.IsCancellationRequested)
            {
                irq.ReceiveResult(result.Item);
            }
        }

        private void DispatchInvocationCompletion(CompletionMessage completion, InvocationRequest irq, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Received Completion for Invocation #{0}", completion.InvocationId);

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            // If the invocation hasn't been cancelled, dispatch the completion
            if (!irq.CancellationToken.IsCancellationRequested)
            {
                if (!string.IsNullOrEmpty(completion.Error))
                {
                    irq.Complete(new Exception(completion.Error));
                }
                else
                {
                    if (completion.HasResult)
                    {
                        irq.ReceiveResult(completion.Result);
                    }

                    irq.Complete();
                }
            }
        }

        private void RemoveRequest(string invocationId)
        {
            lock (_pendingCallsLock)
            {
                if (!_pendingCalls.ContainsKey(invocationId))
                {
                    _logger.LogWarning("Duplicate request to remove invocation {invocationId} from the queue ignored", invocationId);
                }
                else
                {
                    _pendingCalls.Remove(invocationId);
                }
            }
        }

        private void EnqueueRequest(string invocationId, InvocationRequest irq)
        {
            if (_connectionActive.IsCancellationRequested)
            {
                throw new InvalidOperationException("Connection has been terminated.");
            }

            lock (_pendingCallsLock)
            {
                if (_pendingCalls.ContainsKey(invocationId))
                {
                    throw new InvalidOperationException("An invocation with this ID has already been queued");
                }
                _pendingCalls.Add(invocationId, irq);
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
                if (!_connection._pendingCalls.TryGetValue(invocationId, out InvocationRequest irq))
                {
                    _connection._logger.LogError("Unsolicited response received for invocation '{0}'", invocationId);
                    return null;
                }
                return irq.ResultType;
            }

            public Type[] GetParameterTypes(string methodName)
            {
                if (!_connection._handlers.TryGetValue(methodName, out InvocationHandler handler))
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

        private class InvocationRequest : IDisposable
        {
            private readonly TaskCompletionSource<object> _completionSource = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly CancellationTokenRegistration _cancellationTokenRegistration;
            private object _result;
            private object _lock = new object();

            public Type ResultType { get; }
            public CancellationToken CancellationToken { get; }

            public Task<object> Completion => _completionSource.Task;

            public InvocationRequest(CancellationToken cancellationToken, Type resultType)
            {
                CancellationToken = cancellationToken;
                _cancellationTokenRegistration = cancellationToken.Register(() => _completionSource.TrySetCanceled());
                ResultType = resultType;
            }

            public void ReceiveResult(object result)
            {
                lock (_lock)
                {
                    if (Completion.IsCompleted)
                    {
                        throw new InvalidOperationException("Received a result after completion of the invocation");
                    }
                    else
                    {
                        _result = result;
                    }
                }
            }

            public void Complete(Exception ex = null)
            {
                lock (_lock)
                {
                    if (ex != null)
                    {
                        _completionSource.TrySetException(ex);
                    }
                    else
                    {
                        _completionSource.TrySetResult(_result);
                    }
                }
            }

            public void Dispose()
            {
                // Just in case it hasn't already been completed
                _completionSource.TrySetCanceled();

                _cancellationTokenRegistration.Dispose();
            }
        }
    }
}
