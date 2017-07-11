// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Channels;
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

        private readonly object _pendingCallsLock = new object();
        private readonly CancellationTokenSource _connectionActive = new CancellationTokenSource();
        private readonly Dictionary<string, InvocationRequest> _pendingCalls = new Dictionary<string, InvocationRequest>();
        private readonly ConcurrentDictionary<string, InvocationHandler> _handlers = new ConcurrentDictionary<string, InvocationHandler>();

        private int _nextId = 0;

        public event Func<Task> Connected
        {
            add { _connection.Connected += value; }
            remove { _connection.Connected -= value; }
        }

        public event Func<Exception, Task> Closed
        {
            add { _connection.Closed += value; }
            remove { _connection.Closed -= value; }
        }

        public HubConnection(IConnection connection)
            : this(connection, new JsonHubProtocol(new JsonSerializer()), null)
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

            if (protocol == null)
            {
                throw new ArgumentNullException(nameof(protocol));
            }

            _connection = connection;
            _binder = new HubBinder(this);
            _protocol = protocol;
            _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
            _logger = _loggerFactory.CreateLogger<HubConnection>();
            _connection.Received += OnDataReceivedAsync;
            _connection.Closed += Shutdown;
        }

        public async Task StartAsync()
        {
            var transferMode = _protocol.Type == ProtocolType.Binary
                ? TransferMode.Binary
                : TransferMode.Text;

            _connection.Features.Set<ITransferModeFeature>(new TransferModeFeature { TransferMode = transferMode });
            await _connection.StartAsync();

            using (var memoryStream = new MemoryStream())
            {
                NegotiationProtocol.WriteMessage(new NegotiationMessage(_protocol.Name), memoryStream);
                await _connection.SendAsync(memoryStream.ToArray(), _connectionActive.Token);
            }
        }

        public async Task DisposeAsync()
        {
            await _connection.DisposeAsync();
        }

        // TODO: Client return values/tasks?
        public void On(string methodName, Type[] parameterTypes, Func<object[], Task> handler)
        {
            var invocationHandler = new InvocationHandler(parameterTypes, handler);
            _handlers.AddOrUpdate(methodName, invocationHandler, (_, __) => invocationHandler);
        }

        public ReadableChannel<object> Stream(string methodName, Type returnType, CancellationToken cancellationToken, params object[] args)
        {
            var irq = InvocationRequest.Stream(cancellationToken, returnType, GetNextId(), _loggerFactory, out var channel);
            _ = InvokeCore(methodName, irq, args, nonBlocking: false);
            return channel;
        }

        public async Task<object> InvokeAsync(string methodName, Type returnType, CancellationToken cancellationToken, params object[] args)
        {
            var irq = InvocationRequest.Invoke(cancellationToken, returnType, GetNextId(), _loggerFactory, out var task);
            await InvokeCore(methodName, irq, args, nonBlocking: false);
            return await task;
        }

        public Task SendAsync(string methodName, CancellationToken cancellationToken, params object[] args)
        {
            var irq = InvocationRequest.Invoke(cancellationToken, typeof(void), GetNextId(), _loggerFactory, out _);
            return InvokeCore(methodName, irq, args, nonBlocking: true);
        }

        private Task InvokeCore(string methodName, InvocationRequest irq, object[] args, bool nonBlocking)
        {
            ThrowIfConnectionTerminated();
            if (nonBlocking)
            {
                _logger.LogTrace("Preparing invocation of '{target}' and {argumentCount} args", methodName, irq.ResultType.AssemblyQualifiedName, args.Length);
            }
            else
            {
                _logger.LogTrace("Preparing invocation of '{target}', with return type '{returnType}' and {argumentCount} args", methodName, irq.ResultType.AssemblyQualifiedName, args.Length);
            }

            // Create an invocation descriptor. Client invocations are always blocking
            var invocationMessage = new InvocationMessage(irq.InvocationId, nonBlocking, methodName, args);

            // We don't need to track invocations for fire an forget calls
            if (!nonBlocking)
            {
                // I just want an excuse to use 'irq' as a variable name...
                _logger.LogDebug("Registering Invocation ID '{invocationId}' for tracking", invocationMessage.InvocationId);

                AddInvocation(irq);
            }

            // Trace the full invocation, but only if that logging level is enabled (because building the args list is a bit slow)
            if (_logger.IsEnabled(LogLevel.Trace))
            {
                var argsList = string.Join(", ", args.Select(a => a.GetType().FullName));
                _logger.LogTrace("Issuing Invocation '{invocationId}': {returnType} {methodName}({args})", invocationMessage.InvocationId, irq.ResultType.FullName, methodName, argsList);
            }

            // We don't need to wait for this to complete. It will signal back to the invocation request.
            return SendInvocation(invocationMessage, irq);
        }

        private async Task SendInvocation(InvocationMessage invocationMessage, InvocationRequest irq)
        {
            try
            {
                var payload = _protocol.WriteToArray(invocationMessage);

                _logger.LogInformation("Sending Invocation '{invocationId}'", invocationMessage.InvocationId);

                await _connection.SendAsync(payload, irq.CancellationToken);
                _logger.LogInformation("Sending Invocation '{invocationId}' complete", invocationMessage.InvocationId);
            }
            catch (Exception ex)
            {
                _logger.LogError(0, ex, "Sending Invocation '{invocationId}' failed", invocationMessage.InvocationId);
                irq.Fail(ex);
                TryRemoveInvocation(invocationMessage.InvocationId, out _);
            }
        }

        private async Task OnDataReceivedAsync(byte[] data)
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
                            await DispatchInvocationAsync(invocation, _connectionActive.Token);
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

        private Task Shutdown(Exception ex = null)
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
            return Task.CompletedTask;
        }

        private Task DispatchInvocationAsync(InvocationMessage invocation, CancellationToken cancellationToken)
        {
            // Find the handler
            if (!_handlers.TryGetValue(invocation.Target, out InvocationHandler handler))
            {
                _logger.LogWarning("Failed to find handler for '{target}' method", invocation.Target);
                return Task.CompletedTask;
            }

            // TODO: Return values
            // TODO: Dispatch to a sync context to ensure we aren't blocking this loop.
            return handler.Handler(invocation.Arguments);
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
            public Func<object[], Task> Handler { get; }
            public Type[] ParameterTypes { get; }

            public InvocationHandler(Type[] parameterTypes, Func<object[], Task> handler)
            {
                Handler = handler;
                ParameterTypes = parameterTypes;
            }
        }
    }
}
