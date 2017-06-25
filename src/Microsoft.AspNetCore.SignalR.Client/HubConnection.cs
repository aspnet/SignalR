// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Channels;
using Microsoft.AspNetCore.SignalR.Internal;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Microsoft.AspNetCore.Sockets;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;

namespace Microsoft.AspNetCore.SignalR.Client
{
    public class HubConnection : IHubClientProxy
    {
        private readonly ILogger _logger;
        private readonly IConnection _connection;
        private readonly IHubProtocol _protocol;
        private readonly HubBinder _binder;

        private readonly object _pendingCallsLock = new object();
        private readonly CancellationTokenSource _connectionActive = new CancellationTokenSource();
        private readonly Dictionary<string, InvocationRequest> _pendingCalls = new Dictionary<string, InvocationRequest>();
        private readonly ConcurrentDictionary<string, InvocationHandler> _handlers = new ConcurrentDictionary<string, InvocationHandler>();

        private int _nextId = 0;

        public event Action Connected
        {
            add { _connection.Connected += value; }
            remove { _connection.Connected -= value; }
        }

        public void Bind<THub>(THub instance)
        {
            Bind(() => instance);
        }

        public void Bind<THub>(Func<THub> factory)
        {
            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            var hubType = typeof(THub);
            var hubTypeInfo = hubType.GetTypeInfo();
            var methods = new HashSet<string>();

            foreach (var methodInfo in hubType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                var methodName = methodInfo.Name;

                if (!methods.Add(methodName))
                {
                    throw new NotSupportedException($"Duplicate definitions of '{methodName}'. Overloading is not supported.");
                }

                var executor = ObjectMethodExecutor.Create(methodInfo, hubTypeInfo);

                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug("Hub method '{methodName}' is bound", methodName);
                }

                var methodExecutor = executor;

                On(methodName, methodInfo.GetParameters().Select(p => p.ParameterType).ToArray(), methodInfo.ReturnType, async args =>
                {
                    object result = null;
                    var hub = factory();

                    // ReadableChannel is awaitable but we don't want to await it.
                    if (methodExecutor.IsMethodAsync && !IsChannel(methodExecutor.MethodReturnType, out _))
                    {
                        if (methodExecutor.MethodReturnType == typeof(Task))
                        {
                            await (Task)methodExecutor.Execute(hub, args);
                        }
                        else
                        {
                            result = await methodExecutor.ExecuteAsync(hub, args);
                        }
                    }
                    else
                    {
                        result = methodExecutor.Execute(hub, args);
                    }

                    return result;
                });
            }
        }

        public event Action<Exception> Closed
        {
            add { _connection.Closed += value; }
            remove { _connection.Closed -= value; }
        }

        public HubConnection(IConnection connection)
            : this(connection, new JsonHubProtocol(new JsonSerializer()), loggerFactory: NullLoggerFactory.Instance)
        { }

        // These are only really needed for tests now...
        public HubConnection(IConnection connection, ILoggerFactory loggerFactory)
            : this(connection, new JsonHubProtocol(new JsonSerializer()), loggerFactory)
        { }

        public HubConnection(IConnection connection, IHubProtocol protocol, ILoggerFactory loggerFactory)
            : this(connection, protocol, loggerFactory.CreateLogger<HubConnection>())
        { }

        public HubConnection(IConnection connection, IHubProtocol protocol, ILogger logger)
        {
            if (connection == null)
            {
                throw new ArgumentNullException(nameof(connection));
            }

            if (protocol == null)
            {
                throw new ArgumentNullException(nameof(protocol));
            }

            _connection = connection;
            _binder = new HubBinder(this);
            _protocol = protocol;
            _logger = logger;
            _connection.Received += OnDataReceived;
            _connection.Closed += Shutdown;
        }

        public async Task StartAsync()
        {
            await _connection.StartAsync();
        }

        public async Task DisposeAsync()
        {
            await _connection.DisposeAsync();
        }

        public void On(string methodName, Type[] parameterTypes, Type returnType, Func<object[], Task<object>> handler)
        {
            var invocationHandler = new InvocationHandler(parameterTypes, returnType, handler);
            _handlers.AddOrUpdate(methodName, invocationHandler, (_, __) => invocationHandler);
        }

        public ReadableChannel<object> Stream(string methodName, Type returnType, CancellationToken cancellationToken, params object[] args)
        {
            var irq = InvocationRequest.Stream(cancellationToken, returnType, GetNextId(), _logger, out var channel);
            _ = InvokeCore(methodName, irq, args, nonBlocking: false);
            return channel;
        }

        public async Task<object> InvokeAsync(string methodName, Type returnType, CancellationToken cancellationToken, params object[] args)
        {
            var irq = InvocationRequest.Invoke(cancellationToken, returnType, GetNextId(), _logger, out var task);
            await InvokeCore(methodName, irq, args, nonBlocking: false);
            return await task;
        }

        public Task Invoke(string methodName, params object[] args)
        {
            var irq = InvocationRequest.Invoke(CancellationToken.None, resultType: typeof(void), invocationId: GetNextId(), logger: _logger, result: out var task);
            return InvokeCore(methodName, irq, args, nonBlocking: true);
        }

        private Task InvokeCore(string methodName, InvocationRequest irq, object[] args, bool nonBlocking)
        {
            ThrowIfConnectionTerminated();
            _logger.LogTrace("Preparing invocation of '{target}', with return type '{returnType}' and {argumentCount} args", methodName, irq.ResultType.AssemblyQualifiedName ?? "null", args.Length);

            // Create an invocation descriptor. Client invocations are always blocking
            var invocationMessage = new InvocationMessage(irq.InvocationId, nonBlocking, methodName, args);

            if (!nonBlocking)
            {
                // I just want an excuse to use 'irq' as a variable name...
                _logger.LogDebug("Registering Invocation ID '{invocationId}' for tracking", invocationMessage.InvocationId);

                AddInvocation(irq);

                // Trace the full invocation, but only if that logging level is enabled (because building the args list is a bit slow)
                if (_logger.IsEnabled(LogLevel.Trace))
                {
                    var argsList = string.Join(", ", args.Select(a => a.GetType().FullName));
                    _logger.LogTrace("Issuing Invocation '{invocationId}': {returnType} {methodName}({args})", invocationMessage.InvocationId, irq.ResultType.FullName, methodName, argsList);
                }
            }

            // We don't need to wait for this to complete. It will signal back to the invocation request.
            return SendMessageAsync(invocationMessage, irq);
        }

        private async Task SendMessageAsync(HubMessage message, InvocationRequest irq = null)
        {
            try
            {
                var payload = _protocol.WriteToArray(message);

                _logger.LogInformation("Sending message '{invocationId}'", message.InvocationId);

                await _connection.SendAsync(payload, irq?.CancellationToken ?? CancellationToken.None);
                _logger.LogInformation("Sending message '{invocationId}' complete", message.InvocationId);
            }
            catch (Exception ex)
            {
                _logger.LogError(0, ex, "Sending message '{invocationId}' failed", message.InvocationId);

                if (irq != null)
                {
                    irq.Fail(ex);
                    TryRemoveInvocation(message.InvocationId, out _);
                }
            }
        }

        private void OnDataReceived(byte[] data)
        {
            if (_protocol.TryParseMessages(data, _binder, out var messages))
            {
                foreach (var message in messages)
                {
                    InvocationRequest irq;
                    switch (message)
                    {
                        case InvocationMessage invocation:
                            if (_logger.IsEnabled(LogLevel.Trace))
                            {
                                var argsList = string.Join(", ", invocation.Arguments.Select(a => a.GetType().FullName));
                                _logger.LogTrace("Received Invocation '{invocationId}': {methodName}({args})", invocation.InvocationId, invocation.Target, argsList);
                            }
                            _ = DispatchInvocation(invocation, _connectionActive.Token);
                            break;
                        case CompletionMessage completion:
                            if (!TryRemoveInvocation(completion.InvocationId, out irq))
                            {
                                _logger.LogWarning("Dropped unsolicited Completion message for invocation '{invocationId}'", completion.InvocationId);
                                return;
                            }
                            DispatchInvocationCompletion(completion, irq);
                            irq.Dispose();
                            break;
                        case StreamItemMessage streamItem:
                            // Complete the invocation with an error, we don't support streaming (yet)
                            if (!TryGetInvocation(streamItem.InvocationId, out irq))
                            {
                                _logger.LogWarning("Dropped unsolicited Stream Item message for invocation '{invocationId}'", streamItem.InvocationId);
                                return;
                            }
                            DispatchInvocationStreamItemAsync(streamItem, irq);
                            break;
                        default:
                            throw new InvalidOperationException($"Unknown message type: {message.GetType().FullName}");
                    }
                }
            }
        }

        private void Shutdown(Exception ex = null)
        {
            _logger.LogTrace("Shutting down connection");
            if (ex != null)
            {
                _logger.LogError(ex, "Connection is shutting down due to an error");
            }

            lock (_pendingCallsLock)
            {
                // We cancel inside the lock to make sure everyone who was part-way through registering an invocation
                // completes. This also ensures that nobody will add things to _pendingCalls after we leave this block
                // because everything that adds to _pendingCalls checks _connectionActive first (inside the _pendingCallsLock)
                _connectionActive.Cancel();

                foreach (var outstandingCall in _pendingCalls.Values)
                {
                    _logger.LogTrace("Removing pending call {invocationId}", outstandingCall.InvocationId);
                    if (ex != null)
                    {
                        outstandingCall.Fail(ex);
                    }
                    outstandingCall.Dispose();
                }
                _pendingCalls.Clear();
            }
        }

        private async Task DispatchInvocation(InvocationMessage invocationMessage, CancellationToken cancellationToken)
        {
            // Find the handler
            if (!_handlers.TryGetValue(invocationMessage.Target, out var handler))
            {
                _logger.LogWarning("Failed to find handler for '{target}' method", invocationMessage.Target);
                return;
            }

            try
            {
                object result = await handler.Handler(invocationMessage.Arguments);

                if (IsStreamed(result, handler.MethodReturnType, out var enumerator))
                {
                    _logger.LogTrace("[{connectionId}/{invocationId}] Streaming result of type {resultType}", _connection.ConnectionId, invocationMessage.InvocationId, handler.MethodReturnType.FullName);
                    await StreamResultsAsync(invocationMessage.InvocationId, enumerator);
                }
                else if (!invocationMessage.NonBlocking)
                {
                    _logger.LogTrace("[{connectionId}/{invocationId}] Sending result of type {resultType}", _connection.ConnectionId, invocationMessage.InvocationId, handler.MethodReturnType.FullName);
                    await SendMessageAsync(CompletionMessage.WithResult(invocationMessage.InvocationId, result));
                }
            }
            catch (TargetInvocationException ex)
            {
                _logger.LogError(0, ex, "Failed to invoke hub method");

                if (!invocationMessage.NonBlocking)
                {
                    await SendMessageAsync(CompletionMessage.WithError(invocationMessage.InvocationId, ex.InnerException.Message));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(0, ex, "Failed to invoke hub method");
                if (!invocationMessage.NonBlocking)
                {
                    await SendMessageAsync(CompletionMessage.WithError(invocationMessage.InvocationId, ex.Message));
                }
            }
        }

        private bool IsChannel(Type type, out Type payloadType)
        {
            var channelType = type.AllBaseTypes().FirstOrDefault(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(ReadableChannel<>));
            if (channelType == null)
            {
                payloadType = null;
                return false;
            }
            else
            {
                payloadType = channelType.GetGenericArguments()[0];
                return true;
            }
        }

        private async Task StreamResultsAsync(string invocationId, IAsyncEnumerator<object> enumerator)
        {
            // TODO: Cancellation? See https://github.com/aspnet/SignalR/issues/481
            try
            {
                while (await enumerator.MoveNextAsync())
                {
                    // Send the stream item
                    await SendMessageAsync(new StreamItemMessage(invocationId, enumerator.Current));
                }

                await SendMessageAsync(CompletionMessage.Empty(invocationId));
            }
            catch (Exception ex)
            {
                await SendMessageAsync(CompletionMessage.WithError(invocationId, ex.Message));
            }
        }

        private bool IsStreamed(object result, Type resultType, out IAsyncEnumerator<object> enumerator)
        {
            if (result == null)
            {
                enumerator = null;
                return false;
            }

            var observableInterface = IsIObservable(resultType) ?
                resultType :
                resultType.GetInterfaces().FirstOrDefault(IsIObservable);
            if (observableInterface != null)
            {
                enumerator = AsyncEnumeratorAdapters.FromObservable(result, observableInterface);
                return true;
            }
            else if (IsChannel(resultType, out var payloadType))
            {
                enumerator = AsyncEnumeratorAdapters.FromChannel(result, payloadType);
                return true;
            }
            else
            {
                // Not streamed
                enumerator = null;
                return false;
            }
        }

        private static bool IsIObservable(Type iface)
        {
            return iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IObservable<>);
        }

        // This async void is GROSS but we need to dispatch asynchronously because we're writing to a Channel
        // and there's nobody to actually wait for us to finish.
        private async void DispatchInvocationStreamItemAsync(StreamItemMessage streamItem, InvocationRequest irq)
        {
            _logger.LogTrace("Received StreamItem for Invocation #{invocationId}", streamItem.InvocationId);

            if (irq.CancellationToken.IsCancellationRequested)
            {
                _logger.LogTrace("Canceling dispatch of StreamItem message for Invocation {invocationId}. The invocation was cancelled.", irq.InvocationId);
            }
            else if (!await irq.StreamItem(streamItem.Item))
            {
                _logger.LogWarning("Invocation {invocationId} received stream item after channel was closed.", irq.InvocationId);
            }
        }

        private void DispatchInvocationCompletion(CompletionMessage completion, InvocationRequest irq)
        {
            _logger.LogTrace("Received Completion for Invocation #{invocationId}", completion.InvocationId);

            if (irq.CancellationToken.IsCancellationRequested)
            {
                _logger.LogTrace("Cancelling dispatch of Completion message for Invocation {invocationId}. The invocation was cancelled.", irq.InvocationId);
            }
            else
            {
                if (!string.IsNullOrEmpty(completion.Error))
                {
                    irq.Fail(new HubException(completion.Error));
                }
                else
                {
                    irq.Complete(completion.Result);
                }
            }
        }

        private void ThrowIfConnectionTerminated()
        {
            if (_connectionActive.Token.IsCancellationRequested)
            {
                _logger.LogError("Invoke was called after the connection was terminated");
                throw new InvalidOperationException("Connection has been terminated.");
            }
        }

        private string GetNextId() => Interlocked.Increment(ref _nextId).ToString();

        private void AddInvocation(InvocationRequest irq)
        {
            lock (_pendingCallsLock)
            {
                ThrowIfConnectionTerminated();
                if (_pendingCalls.ContainsKey(irq.InvocationId))
                {
                    _logger.LogCritical("Invocation ID '{invocationId}' is already in use.", irq.InvocationId);
                    throw new InvalidOperationException($"Invocation ID '{irq.InvocationId}' is already in use.");
                }
                else
                {
                    _pendingCalls.Add(irq.InvocationId, irq);
                }
            }
        }

        private bool TryGetInvocation(string invocationId, out InvocationRequest irq)
        {
            lock (_pendingCallsLock)
            {
                ThrowIfConnectionTerminated();
                return _pendingCalls.TryGetValue(invocationId, out irq);
            }
        }

        private bool TryRemoveInvocation(string invocationId, out InvocationRequest irq)
        {
            lock (_pendingCallsLock)
            {
                ThrowIfConnectionTerminated();
                if (_pendingCalls.TryGetValue(invocationId, out irq))
                {
                    _pendingCalls.Remove(invocationId);
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }

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
                    _connection._logger.LogError("Unsolicited response received for invocation '{invocationId}'", invocationId);
                    return null;
                }
                return irq.ResultType;
            }

            public Type[] GetParameterTypes(string methodName)
            {
                if (!_connection._handlers.TryGetValue(methodName, out InvocationHandler handler))
                {
                    _connection._logger.LogWarning("Failed to find handler for '{target}' method", methodName);
                    return Type.EmptyTypes;
                }
                return handler.ParameterTypes;
            }
        }

        private struct InvocationHandler
        {
            public Func<object[], Task<object>> Handler { get; }
            public Type[] ParameterTypes { get; }
            public Type MethodReturnType { get; }

            public InvocationHandler(Type[] parameterTypes, Type returnType, Func<object[], Task<object>> handler)
            {
                ParameterTypes = parameterTypes;
                Handler = handler;
                MethodReturnType = returnType;
            }
        }
    }
}
