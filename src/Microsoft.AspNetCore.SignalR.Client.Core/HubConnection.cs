// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Channels;
using Microsoft.AspNetCore.SignalR.Internal;
using Microsoft.AspNetCore.SignalR.Internal.Encoders;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Microsoft.AspNetCore.Sockets;
using Microsoft.AspNetCore.Sockets.Client;
using Microsoft.AspNetCore.Sockets.Features;
using Microsoft.AspNetCore.Sockets.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.AspNetCore.SignalR.Client
{
    public partial class HubConnection
    {
        public static readonly TimeSpan DefaultServerTimeout = TimeSpan.FromSeconds(30); // Server ping rate is 15 sec, this is 2 times that.

        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger _logger;
        private readonly IConnection _connection;
        private readonly IHubProtocol _protocol;
        private readonly HubBinder _binder;
        private HubProtocolReaderWriter _protocolReaderWriter;

        private readonly object _pendingCallsLock = new object();
        private readonly Dictionary<string, InvocationRequest> _pendingCalls = new Dictionary<string, InvocationRequest>();
        private readonly ConcurrentDictionary<string, InvocationHandlerList> _handlers = new ConcurrentDictionary<string, InvocationHandlerList>();
        private CancellationTokenSource _connectionActive;

        private int _nextId = 0;
        private volatile bool _startCalled;
        private Timer _timeoutTimer;
        private bool _needKeepAlive;

        public event Action<Exception> Closed;

        /// <summary>
        /// Gets or sets the server timeout interval for the connection. Changes to this value
        /// will not be applied until the Keep Alive timer is next reset.
        /// </summary>
        public TimeSpan ServerTimeout { get; set; } = DefaultServerTimeout;

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
            _connection.OnReceived((data, state) => ((HubConnection)state).OnDataReceivedAsync(data), this);
            _connection.Closed += e => Shutdown(e);

            // Create the timer for timeout, but disabled by default (we enable it when started).
            _timeoutTimer = new Timer(state => ((HubConnection)state).TimeoutElapsed(), this, Timeout.Infinite, Timeout.Infinite);
        }

        public async Task StartAsync()
        {
            try
            {
                await StartAsyncCore().ForceAsync();
            }
            finally
            {
                _startCalled = true;
            }
        }

        private void TimeoutElapsed()
        {
            _connection.AbortAsync(new TimeoutException($"Server timeout ({ServerTimeout.TotalMilliseconds:0.00}ms) elapsed without receiving a message from the server."));
        }

        private void ResetTimeoutTimer()
        {
            if (_needKeepAlive)
            {
                Log.ResettingKeepAliveTimer(_logger);

                // If the connection is disposed while this callback is firing, or if the callback is fired after dispose
                // (which can happen because of some races), this will throw ObjectDisposedException. That's OK, because
                // we don't need the timer anyway.
                try
                {
                    _timeoutTimer.Change(ServerTimeout, Timeout.InfiniteTimeSpan);
                }
                catch (ObjectDisposedException)
                {
                    // This is OK!
                }
            }
        }

        private async Task StartAsyncCore()
        {
            var transferModeFeature = _connection.Features.Get<ITransferModeFeature>();
            if (transferModeFeature == null)
            {
                transferModeFeature = new TransferModeFeature();
                _connection.Features.Set(transferModeFeature);
            }

            var requestedTransferMode =
                _protocol.Type == ProtocolType.Binary
                    ? TransferMode.Binary
                    : TransferMode.Text;

            transferModeFeature.TransferMode = requestedTransferMode;
            await _connection.StartAsync();
            _needKeepAlive = _connection.Features.Get<IConnectionInherentKeepAliveFeature>() == null;

            var actualTransferMode = transferModeFeature.TransferMode;

            _protocolReaderWriter = new HubProtocolReaderWriter(_protocol, GetDataEncoder(requestedTransferMode, actualTransferMode));

            Log.HubProtocol(_logger, _protocol.Name);

            _connectionActive = new CancellationTokenSource();
            using (var memoryStream = new MemoryStream())
            {
                Log.SendingHubNegotiate(_logger);
                NegotiationProtocol.WriteMessage(new NegotiationMessage(_protocol.Name), memoryStream);
                await _connection.SendAsync(memoryStream.ToArray(), _connectionActive.Token);
            }

            ResetTimeoutTimer();
        }

        private IDataEncoder GetDataEncoder(TransferMode requestedTransferMode, TransferMode actualTransferMode)
        {
            if (requestedTransferMode == TransferMode.Binary && actualTransferMode == TransferMode.Text)
            {
                // This is for instance for SSE which is a Text protocol and the user wants to use a binary
                // protocol so we need to encode messages.
                return new Base64Encoder();
            }

            Debug.Assert(requestedTransferMode == actualTransferMode, "All transports besides SSE are expected to support binary mode.");

            return new PassThroughEncoder();
        }

        public async Task StopAsync() => await StopAsyncCore().ForceAsync();

        private Task StopAsyncCore() => _connection.StopAsync();

        public async Task DisposeAsync() => await DisposeAsyncCore().ForceAsync();

        private async Task DisposeAsyncCore()
        {
            await _connection.DisposeAsync();

            // Dispose the timer AFTER shutting down the connection.
            _timeoutTimer.Dispose();
        }

        // TODO: Client return values/tasks?
        public IDisposable On(string methodName, Type[] parameterTypes, Func<object[], object, Task> handler, object state)
        {
            var invocationHandler = new InvocationHandler(parameterTypes, handler, state);
            var invocationList = _handlers.AddOrUpdate(methodName,
                _ => new InvocationHandlerList(invocationHandler),
                (_, invocations) => invocations.Add(invocationHandler));

            return new Subscription(invocationHandler, invocationList);
        }

        public async Task<ChannelReader<object>> StreamAsync(string methodName, Type returnType, object[] args, CancellationToken cancellationToken = default)
        {
            return await StreamAsyncCore(methodName, returnType, args, cancellationToken).ForceAsync();
        }

        private async Task<ChannelReader<object>> StreamAsyncCore(string methodName, Type returnType, object[] args, CancellationToken cancellationToken)
        {
            if (!_startCalled)
            {
                throw new InvalidOperationException($"The '{nameof(StreamAsync)}' method cannot be called before the connection has been started.");
            }

            var invokeCts = new CancellationTokenSource();
            var irq = InvocationRequest.Stream(invokeCts.Token, returnType, GetNextId(), _loggerFactory, this, out var channel);
            // After InvokeCore we don't want the irq cancellation token to be triggered.
            // The stream invocation will be canceled by the CancelInvocationMessage, connection closing, or channel finishing.
            using (cancellationToken.Register(token => ((CancellationTokenSource)token).Cancel(), invokeCts))
            {
                await InvokeStreamCore(methodName, irq, args);
            }

            if (cancellationToken.CanBeCanceled)
            {
                cancellationToken.Register(state =>
                {
                    var invocationReq = (InvocationRequest)state;
                    if (!invocationReq.HubConnection._connectionActive.IsCancellationRequested)
                    {
                        // Fire and forget, if it fails that means we aren't connected anymore.
                        _ = invocationReq.HubConnection.SendHubMessage(new CancelInvocationMessage(invocationReq.InvocationId), invocationReq);

                        if (invocationReq.HubConnection.TryRemoveInvocation(invocationReq.InvocationId, out _))
                        {
                            invocationReq.Complete(CompletionMessage.Empty(irq.InvocationId));
                        }

                        invocationReq.Dispose();
                    }
                }, irq);
            }

            return channel;
        }

        public async Task<object> InvokeAsync(string methodName, Type returnType, object[] args, CancellationToken cancellationToken = default) =>
             await InvokeAsyncCore(methodName, returnType, args, cancellationToken).ForceAsync();

        private async Task<object> InvokeAsyncCore(string methodName, Type returnType, object[] args, CancellationToken cancellationToken)
        {
            if (!_startCalled)
            {
                throw new InvalidOperationException($"The '{nameof(InvokeAsync)}' method cannot be called before the connection has been started.");
            }

            var irq = InvocationRequest.Invoke(cancellationToken, returnType, GetNextId(), _loggerFactory, this, out var task);
            await InvokeCore(methodName, irq, args);
            return await task;
        }

        private Task InvokeCore(string methodName, InvocationRequest irq, object[] args)
        {
            ThrowIfConnectionTerminated(irq.InvocationId);
            Log.PreparingBlockingInvocation(_logger, irq.InvocationId, methodName, irq.ResultType.FullName, args.Length);

            // Client invocations are always blocking
            var invocationMessage = new InvocationMessage(irq.InvocationId, target: methodName,
                argumentBindingException: null, arguments: args);

            Log.RegisterInvocation(_logger, invocationMessage.InvocationId);

            AddInvocation(irq);

            // Trace the full invocation
            Log.IssueInvocation(_logger, invocationMessage.InvocationId, irq.ResultType.FullName, methodName, args);

            // We don't need to wait for this to complete. It will signal back to the invocation request.
            return SendHubMessage(invocationMessage, irq);
        }

        private Task InvokeStreamCore(string methodName, InvocationRequest irq, object[] args)
        {
            ThrowIfConnectionTerminated(irq.InvocationId);

            Log.PreparingStreamingInvocation(_logger, irq.InvocationId, methodName, irq.ResultType.FullName, args.Length);

            var invocationMessage = new StreamInvocationMessage(irq.InvocationId, methodName,
                argumentBindingException: null, arguments: args);

            // I just want an excuse to use 'irq' as a variable name...
            Log.RegisterInvocation(_logger, invocationMessage.InvocationId);

            AddInvocation(irq);

            // Trace the full invocation
            Log.IssueInvocation(_logger, invocationMessage.InvocationId, irq.ResultType.FullName, methodName, args);

            // We don't need to wait for this to complete. It will signal back to the invocation request.
            return SendHubMessage(invocationMessage, irq);
        }

        private async Task SendHubMessage(HubInvocationMessage hubMessage, InvocationRequest irq)
        {
            try
            {
                var payload = _protocolReaderWriter.WriteMessage(hubMessage);
                Log.SendInvocation(_logger, hubMessage.InvocationId);

                await _connection.SendAsync(payload, irq.CancellationToken);
                Log.SendInvocationCompleted(_logger, hubMessage.InvocationId);
            }
            catch (Exception ex)
            {
                Log.SendInvocationFailed(_logger, hubMessage.InvocationId, ex);
                irq.Fail(ex);
                TryRemoveInvocation(hubMessage.InvocationId, out _);
            }
        }

        public async Task SendAsync(string methodName, object[] args, CancellationToken cancellationToken = default) =>
            await SendAsyncCore(methodName, args, cancellationToken).ForceAsync();

        private async Task SendAsyncCore(string methodName, object[] args, CancellationToken cancellationToken)
        {
            if (!_startCalled)
            {
                throw new InvalidOperationException($"The '{nameof(SendAsync)}' method cannot be called before the connection has been started.");
            }

            var invocationMessage = new InvocationMessage(null, target: methodName,
                argumentBindingException: null, arguments: args);

            ThrowIfConnectionTerminated(invocationMessage.InvocationId);

            try
            {
                Log.PreparingNonBlockingInvocation(_logger, methodName, args.Length);

                var payload = _protocolReaderWriter.WriteMessage(invocationMessage);
                Log.SendInvocation(_logger, invocationMessage.InvocationId);

                await _connection.SendAsync(payload, cancellationToken);
                Log.SendInvocationCompleted(_logger, invocationMessage.InvocationId);
            }
            catch (Exception ex)
            {
                Log.SendInvocationFailed(_logger, invocationMessage.InvocationId, ex);
                throw;
            }
        }

        private async Task OnDataReceivedAsync(byte[] data)
        {
            ResetTimeoutTimer();
            Log.ParsingMessages(_logger, data.Length);
            if (_protocolReaderWriter.ReadMessages(data, _binder, out var messages))
            {
                Log.ReceivingMessages(_logger, messages.Count);
                foreach (var message in messages)
                {
                    InvocationRequest irq;
                    switch (message)
                    {
                        case InvocationMessage invocation:
                            Log.ReceivedInvocation(_logger, invocation.InvocationId, invocation.Target,
                                invocation.ArgumentBindingException != null ? null : invocation.Arguments);
                            await DispatchInvocationAsync(invocation, _connectionActive.Token);
                            break;
                        case CompletionMessage completion:
                            if (!TryRemoveInvocation(completion.InvocationId, out irq))
                            {
                                Log.DropCompletionMessage(_logger, completion.InvocationId);
                                return;
                            }
                            DispatchInvocationCompletion(completion, irq);
                            irq.Dispose();
                            break;
                        case StreamItemMessage streamItem:
                            // Complete the invocation with an error, we don't support streaming (yet)
                            if (!TryGetInvocation(streamItem.InvocationId, out irq))
                            {
                                Log.DropStreamMessage(_logger, streamItem.InvocationId);
                                return;
                            }
                            DispatchInvocationStreamItemAsync(streamItem, irq);
                            break;
                        case PingMessage _:
                            Log.ReceivedPing(_logger);
                            // Nothing to do on receipt of a ping.
                            break;
                        default:
                            throw new InvalidOperationException($"Unexpected message type: {message.GetType().FullName}");
                    }
                }
                Log.ProcessedMessages(_logger, messages.Count);
            }
            else
            {
                Log.FailedParsing(_logger, data.Length);
            }
        }

        private void Shutdown(Exception exception = null)
        {
            Log.ShutdownConnection(_logger);
            if (exception != null)
            {
                Log.ShutdownWithError(_logger, exception);
            }

            lock (_pendingCallsLock)
            {
                // We cancel inside the lock to make sure everyone who was part-way through registering an invocation
                // completes. This also ensures that nobody will add things to _pendingCalls after we leave this block
                // because everything that adds to _pendingCalls checks _connectionActive first (inside the _pendingCallsLock)
                _connectionActive.Cancel();

                foreach (var outstandingCall in _pendingCalls.Values)
                {
                    Log.RemoveInvocation(_logger, outstandingCall.InvocationId);
                    if (exception != null)
                    {
                        outstandingCall.Fail(exception);
                    }
                    outstandingCall.Dispose();
                }
                _pendingCalls.Clear();
            }

            try
            {
                Closed?.Invoke(exception);
            }
            catch (Exception ex)
            {
                Log.ErrorDuringClosedEvent(_logger, ex);
            }
        }

        private async Task DispatchInvocationAsync(InvocationMessage invocation, CancellationToken cancellationToken)
        {
            // Find the handler
            if (!_handlers.TryGetValue(invocation.Target, out var handlers))
            {
                Log.MissingHandler(_logger, invocation.Target);
                return;
            }

            InvocationHandler[] copiedHandlers = handlers.GetCopiedHandlers();

            foreach (var handler in copiedHandlers)
            {
                try
                {
                    await handler.InvokeAsync(invocation.Arguments);
                }
                catch (Exception ex)
                {
                    Log.ErrorInvokingClientSideMethod(_logger, invocation.Target, ex);
                }
            }
        }

        // This async void is GROSS but we need to dispatch asynchronously because we're writing to a Channel
        // and there's nobody to actually wait for us to finish.
        private async void DispatchInvocationStreamItemAsync(StreamItemMessage streamItem, InvocationRequest irq)
        {
            Log.ReceivedStreamItem(_logger, streamItem.InvocationId);

            if (irq.CancellationToken.IsCancellationRequested)
            {
                Log.CancelingStreamItem(_logger, irq.InvocationId);
            }
            else if (!await irq.StreamItem(streamItem.Item))
            {
                Log.ReceivedStreamItemAfterClose(_logger, irq.InvocationId);
            }
        }

        private void DispatchInvocationCompletion(CompletionMessage completion, InvocationRequest irq)
        {
            Log.ReceivedInvocationCompletion(_logger, completion.InvocationId);

            if (irq.CancellationToken.IsCancellationRequested)
            {
                Log.CancelingInvocationCompletion(_logger, irq.InvocationId);
            }
            else
            {
                irq.Complete(completion);
            }
        }

        private void ThrowIfConnectionTerminated(string invocationId)
        {
            if (_connectionActive.Token.IsCancellationRequested)
            {
                Log.InvokeAfterTermination(_logger, invocationId);
                throw new InvalidOperationException("Connection has been terminated.");
            }
        }

        private string GetNextId() => Interlocked.Increment(ref _nextId).ToString();

        private void AddInvocation(InvocationRequest irq)
        {
            lock (_pendingCallsLock)
            {
                ThrowIfConnectionTerminated(irq.InvocationId);
                if (_pendingCalls.ContainsKey(irq.InvocationId))
                {
                    Log.InvocationAlreadyInUse(_logger, irq.InvocationId);
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
                ThrowIfConnectionTerminated(invocationId);
                return _pendingCalls.TryGetValue(invocationId, out irq);
            }
        }

        private bool TryRemoveInvocation(string invocationId, out InvocationRequest irq)
        {
            lock (_pendingCallsLock)
            {
                ThrowIfConnectionTerminated(invocationId);
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

        private class Subscription : IDisposable
        {
            private readonly InvocationHandler _handler;
            private readonly InvocationHandlerList _handlerList;

            public Subscription(InvocationHandler handler, InvocationHandlerList handlerList)
            {
                _handler = handler;
                _handlerList = handlerList;
            }

            public void Dispose()
            {
                _handlerList.Remove(_handler);
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
                if (!_connection._pendingCalls.TryGetValue(invocationId, out var irq))
                {
                    Log.ReceivedUnexpectedResponse(_connection._logger, invocationId);
                    return null;
                }
                return irq.ResultType;
            }

            public IReadOnlyList<Type> GetParameterTypes(string methodName)
            {
                if (!_connection._handlers.TryGetValue(methodName, out var handlers))
                {
                    Log.MissingHandler(_connection._logger, methodName);
                    return Type.EmptyTypes;
                }

                // We use the parameter types of the first handler                
                var copiedHandlers = handlers.GetCopiedHandlers();
                if (copiedHandlers.Length > 0) {
                    return copiedHandlers[0].ParameterTypes;
                }
                throw new InvalidOperationException($"There are no callbacks registered for the method '{methodName}'");
            }
        }

        private struct InvocationHandler
        {
            public Type[] ParameterTypes { get; }
            private readonly Func<object[], object, Task> _callback;
            private readonly object _state;

            public InvocationHandler(Type[] parameterTypes, Func<object[], object, Task> callback, object state)
            {
                _callback = callback;
                ParameterTypes = parameterTypes;
                _state = state;
            }

            public Task InvokeAsync(object[] parameters)
            {
                return _callback(parameters, _state);
            }
        }

        private class InvocationHandlerList
        {
            private readonly List<InvocationHandler> _invocationHandlers;
            private InvocationHandler[] _CopiedHandlers;

            internal InvocationHandlerList(InvocationHandler handler)
            {
                _invocationHandlers = new List<InvocationHandler>() { handler };
            }

            internal InvocationHandler[] GetCopiedHandlers()
            {
                if (_CopiedHandlers == null)
                {
                    lock(_invocationHandlers)
                    {
                        _CopiedHandlers = _invocationHandlers.ToArray();
                    }
                }
                return _CopiedHandlers;
            }

            internal InvocationHandlerList Add(InvocationHandler handler)
            {
                lock (_invocationHandlers)
                {
                    _invocationHandlers.Add(handler);
                    _CopiedHandlers = null;
                }
                return this;
            }
            
            internal void Remove(InvocationHandler handler)
            {
                lock (_invocationHandlers)
                {
                    if (_invocationHandlers.Remove(handler))
                    {
                        _CopiedHandlers = null;
                    }
                }
            }
        }

        private class TransferModeFeature : ITransferModeFeature
        {
            public TransferMode TransferMode { get; set; }
        }
    }
}
