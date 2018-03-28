// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.SignalR.Internal;
using Microsoft.AspNetCore.SignalR.Internal.Formatters;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Microsoft.AspNetCore.Sockets.Client;
using Microsoft.AspNetCore.Sockets.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.AspNetCore.SignalR.Client
{
    public partial class HubConnection
    {
        public static readonly TimeSpan DefaultServerTimeout = TimeSpan.FromSeconds(30); // Server ping rate is 15 sec, this is 2 times that.

        // This lock protects the connection state.
        private readonly SemaphoreSlim _connectionLock = new SemaphoreSlim(1, 1);

        // Persistent across all connections
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger _logger;
        private readonly IHubProtocol _protocol;
        private readonly Func<IConnection> _connectionFactory;
        private readonly ConcurrentDictionary<string, List<InvocationHandler>> _handlers = new ConcurrentDictionary<string, List<InvocationHandler>>();
        private bool _disposed;

        // Transient state to a connection
        private readonly object _pendingCallsLock = new object();
        private ConnectionState _connectionState;

        public event Action<Exception> Closed;

        /// <summary>
        /// Gets or sets the server timeout interval for the connection. Changes to this value
        /// will not be applied until the Keep Alive timer is next reset.
        /// </summary>
        public TimeSpan ServerTimeout { get; set; } = DefaultServerTimeout;

        public HubConnection(Func<IConnection> connectionFactory, IHubProtocol protocol) : this(connectionFactory, protocol, NullLoggerFactory.Instance)
        {
        }

        public HubConnection(Func<IConnection> connectionFactory, IHubProtocol protocol, ILoggerFactory loggerFactory)
        {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
            _protocol = protocol ?? throw new ArgumentNullException(nameof(protocol));

            _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
            _logger = _loggerFactory.CreateLogger<HubConnection>();
        }

        public async Task StartAsync()
        {
            CheckDisposed();
            await StartAsyncCore().ForceAsync();
        }

        public async Task StopAsync()
        {
            CheckDisposed();
            await StopAsyncCore(disposing: false).ForceAsync();
        }

        public async Task DisposeAsync()
        {
            if (!_disposed)
            {
                await StopAsyncCore(disposing: true).ForceAsync();
            }
        }

        public IDisposable On(string methodName, Type[] parameterTypes, Func<object[], object, Task> handler, object state)
        {
            Log.RegisteringHandler(_logger, methodName);

            CheckDisposed();

            // It's OK to be disposed while registering a callback, we'll just never call the callback anyway (as with all the callbacks registered before disposal).
            var invocationHandler = new InvocationHandler(parameterTypes, handler, state);
            var invocationList = _handlers.AddOrUpdate(methodName, _ => new List<InvocationHandler> { invocationHandler },
                (_, invocations) =>
                {
                    lock (invocations)
                    {
                        invocations.Add(invocationHandler);
                    }
                    return invocations;
                });

            return new Subscription(invocationHandler, invocationList);
        }

        public async Task<ChannelReader<object>> StreamAsChannelAsync(string methodName, Type returnType, object[] args, CancellationToken cancellationToken = default) =>
            await StreamAsChannelAsyncCore(methodName, returnType, args, cancellationToken).ForceAsync();

        public async Task<object> InvokeAsync(string methodName, Type returnType, object[] args, CancellationToken cancellationToken = default) =>
            await InvokeAsyncCore(methodName, returnType, args, cancellationToken).ForceAsync();

        // REVIEW: We don't generally use cancellation tokens when writing to a pipe because the asynchrony is only the result of backpressure.
        // However, this would be the only "invocation" method _without_ a cancellation token... which is odd.
        public async Task SendAsync(string methodName, object[] args, CancellationToken cancellationToken = default) =>
            await SendAsyncCore(methodName, args, cancellationToken).ForceAsync();

        private async Task StartAsyncCore()
        {
            await WaitConnectionLockAsync();
            try
            {
                if (_connectionState != null)
                {
                    // We're already connected
                    return;
                }

                CheckDisposed();

                Log.Starting(_logger);

                // Start the connection
                var connection = _connectionFactory();
                await connection.StartAsync(_protocol.TransferFormat);
                _connectionState = new ConnectionState(connection, this);

                // From here on, if an error occurs we need to shut down the connection because
                // we still own it.
                try
                {
                    Log.HubProtocol(_logger, _protocol.Name, _protocol.Version);
                    await HandshakeAsync();
                }
                catch (Exception ex)
                {
                    Log.ErrorStartingConnection(_logger, ex);

                    // Can't have any invocations to cancel, we're in the lock.
                    await _connectionState.Connection.DisposeAsync();
                    throw;
                }

                _connectionState.ReceiveTask = ReceiveLoop(_connectionState);
                Log.Started(_logger);
            }
            finally
            {
                ReleaseConnectionLock();
            }
        }

        // This method does both Dispose and Start, the 'disposing' flag indicates which.
        // The behaviors are nearly identical, except that the _disposed flag is set in the lock
        // if we're disposing.
        private async Task StopAsyncCore(bool disposing)
        {
            // Block a Start from happening until we've finished capturing the connection state.
            ConnectionState connectionState;
            await WaitConnectionLockAsync();
            try
            {
                if (disposing && _disposed)
                {
                    // DisposeAsync should be idempotent.
                    return;
                }

                CheckDisposed();
                connectionState = _connectionState;
                
                // Set the stopping flag so that any invocations after this get a useful error message instead of
                // silently failing or throwing an error about the pipe being completed.
                if (connectionState != null)
                {
                    connectionState.Stopping = true;
                }

                if (disposing)
                {
                    _disposed = true;
                }
            }
            finally
            {
                ReleaseConnectionLock();
            }

            // Now stop the connection we captured
            if (connectionState != null)
            {
                await connectionState.StopAsync(ServerTimeout);
            }
        }

        private async Task<ChannelReader<object>> StreamAsChannelAsyncCore(string methodName, Type returnType, object[] args, CancellationToken cancellationToken)
        {
            async Task OnStreamCancelled(InvocationRequest irq)
            {
                // We need to take the connection lock in order to ensure we a) have a connection and b) are the only one accessing the write end of the pipe.
                await WaitConnectionLockAsync();
                try
                {
                    if (_connectionState != null)
                    {
                        Log.SendingCancellation(_logger, irq.InvocationId);

                        // Fire and forget, if it fails that means we aren't connected anymore.
                        _ = SendHubMessage(new CancelInvocationMessage(irq.InvocationId), irq.CancellationToken);
                    }
                    else
                    {
                        Log.UnableToSendCancellation(_logger, irq.InvocationId);
                    }
                }
                finally
                {
                    ReleaseConnectionLock();
                }

                // Cancel the invocation
                irq.Dispose();
            }

            CheckDisposed();
            await WaitConnectionLockAsync();

            ChannelReader<object> channel;
            try
            {
                CheckDisposed();
                CheckConnectionActive(nameof(StreamAsChannelAsync));

                var irq = InvocationRequest.Stream(cancellationToken, returnType, _connectionState.GetNextId(), _loggerFactory, this, out channel);
                await InvokeStreamCore(methodName, irq, args, cancellationToken);

                if (cancellationToken.CanBeCanceled)
                {
                    cancellationToken.Register(state => _ = OnStreamCancelled((InvocationRequest)state), irq);
                }
            }
            finally
            {
                ReleaseConnectionLock();
            }

            return channel;
        }


        private async Task<object> InvokeAsyncCore(string methodName, Type returnType, object[] args, CancellationToken cancellationToken)
        {
            CheckDisposed();
            await WaitConnectionLockAsync();

            Task<object> invocationTask;
            try
            {
                CheckDisposed();
                CheckConnectionActive(nameof(InvokeAsync));

                var irq = InvocationRequest.Invoke(cancellationToken, returnType, _connectionState.GetNextId(), _loggerFactory, this, out invocationTask);
                await InvokeCore(methodName, irq, args, cancellationToken);
            }
            finally
            {
                ReleaseConnectionLock();
            }

            // Wait for this outside the lock, because it won't complete until the server responds.
            return await invocationTask;
        }

        private async Task InvokeCore(string methodName, InvocationRequest irq, object[] args, CancellationToken cancellationToken)
        {
            AssertConnectionValid();

            Log.PreparingBlockingInvocation(_logger, irq.InvocationId, methodName, irq.ResultType.FullName, args.Length);

            // Client invocations are always blocking
            var invocationMessage = new InvocationMessage(irq.InvocationId, target: methodName,
                argumentBindingException: null, arguments: args);

            Log.RegisteringInvocation(_logger, invocationMessage.InvocationId);

            _connectionState.AddInvocation(irq);

            // Trace the full invocation
            Log.IssuingInvocation(_logger, invocationMessage.InvocationId, irq.ResultType.FullName, methodName, args);

            try
            {
                await SendHubMessage(invocationMessage, cancellationToken);
            }
            catch (Exception ex)
            {
                Log.FailedToSendInvocation(_logger, invocationMessage.InvocationId, ex);
                _connectionState.TryRemoveInvocation(invocationMessage.InvocationId, out _);
                irq.Fail(ex);
            }
        }

        private async Task InvokeStreamCore(string methodName, InvocationRequest irq, object[] args, CancellationToken cancellationToken)
        {
            AssertConnectionValid();

            Log.PreparingStreamingInvocation(_logger, irq.InvocationId, methodName, irq.ResultType.FullName, args.Length);

            var invocationMessage = new StreamInvocationMessage(irq.InvocationId, methodName,
                argumentBindingException: null, arguments: args);

            // I just want an excuse to use 'irq' as a variable name...
            Log.RegisteringInvocation(_logger, invocationMessage.InvocationId);

            _connectionState.AddInvocation(irq);

            // Trace the full invocation
            Log.IssuingInvocation(_logger, invocationMessage.InvocationId, irq.ResultType.FullName, methodName, args);

            try
            {
                await SendHubMessage(invocationMessage, cancellationToken);
            }
            catch (Exception ex)
            {
                Log.FailedToSendInvocation(_logger, invocationMessage.InvocationId, ex);
                _connectionState.TryRemoveInvocation(invocationMessage.InvocationId, out _);
                irq.Fail(ex);
            }
        }

        private async Task SendHubMessage(HubInvocationMessage hubMessage, CancellationToken cancellationToken = default)
        {
            AssertConnectionValid();

            var payload = _protocol.WriteToArray(hubMessage);

            Log.SendingMessage(_logger, hubMessage);
            // REVIEW: If a token is passed in and is cancelled during FlushAsync it seems to break .Complete()...
            await WriteAsync(payload, CancellationToken.None);
            Log.MessageSent(_logger, hubMessage);
        }

        private async Task SendAsyncCore(string methodName, object[] args, CancellationToken cancellationToken)
        {
            CheckDisposed();

            await WaitConnectionLockAsync();
            try
            {
                CheckDisposed();
                CheckConnectionActive(nameof(SendAsync));

                Log.PreparingNonBlockingInvocation(_logger, methodName, args.Length);

                var invocationMessage = new InvocationMessage(null, target: methodName,
                    argumentBindingException: null, arguments: args);

                await SendHubMessage(invocationMessage, cancellationToken);
            }
            finally
            {
                ReleaseConnectionLock();
            }
        }

        private async Task<(bool close, Exception exception)> ProcessMessagesAsync(ReadOnlySequence<byte> buffer, ConnectionState connectionState)
        {
            Log.ProcessingMessage(_logger, buffer.Length);

            // TODO: Don't ToArray it :)
            var data = buffer.ToArray();

            var currentData = new ReadOnlyMemory<byte>(data);
            Log.ParsingMessages(_logger, currentData.Length);

            var messages = new List<HubMessage>();
            if (_protocol.TryParseMessages(currentData, connectionState, messages))
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
                            await DispatchInvocationAsync(invocation);
                            break;
                        case CompletionMessage completion:
                            if (!connectionState.TryRemoveInvocation(completion.InvocationId, out irq))
                            {
                                Log.DroppedCompletionMessage(_logger, completion.InvocationId);
                            }
                            else
                            {
                                DispatchInvocationCompletion(completion, irq);
                                irq.Dispose();
                            }
                            break;
                        case StreamItemMessage streamItem:
                            // Complete the invocation with an error, we don't support streaming (yet)
                            if (!connectionState.TryGetInvocation(streamItem.InvocationId, out irq))
                            {
                                Log.DroppedStreamMessage(_logger, streamItem.InvocationId);
                                return (close: false, exception: null);
                            }
                            await DispatchInvocationStreamItemAsync(streamItem, irq);
                            break;
                        case CloseMessage close:
                            if (string.IsNullOrEmpty(close.Error))
                            {
                                Log.ReceivedClose(_logger);
                                return (close: true, exception: null);
                            }
                            else
                            {
                                Log.ReceivedCloseWithError(_logger, close.Error);
                                return (close: true, exception: new HubException($"The server closed the connection with the following error: {close.Error}"));
                            }
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

            return (close: false, exception: null);
        }

        private async Task DispatchInvocationAsync(InvocationMessage invocation)
        {
            // Find the handler
            if (!_handlers.TryGetValue(invocation.Target, out var handlers))
            {
                Log.MissingHandler(_logger, invocation.Target);
                return;
            }

            // TODO: Optimize this!
            // Copying the callbacks to avoid concurrency issues
            InvocationHandler[] copiedHandlers;
            lock (handlers)
            {
                copiedHandlers = new InvocationHandler[handlers.Count];
                handlers.CopyTo(copiedHandlers);
            }

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

        private async Task DispatchInvocationStreamItemAsync(StreamItemMessage streamItem, InvocationRequest irq)
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

        private void CheckDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(HubConnection));
            }
        }

        private async Task HandshakeAsync()
        {
            // Send the Handshake request
            using (var memoryStream = new MemoryStream())
            {
                Log.SendingHubHandshake(_logger);
                HandshakeProtocol.WriteRequestMessage(new HandshakeRequestMessage(_protocol.Name, _protocol.Version), memoryStream);
                var result = await WriteAsync(memoryStream.ToArray(), CancellationToken.None);

                if (result.IsCompleted)
                {
                    // The other side disconnected
                    throw new InvalidOperationException("The server disconnected before the handshake was completed");
                }
            }

            try
            {
                while (true)
                {
                    var result = await _connectionState.Connection.Transport.Input.ReadAsync();
                    var buffer = result.Buffer;
                    var consumed = buffer.Start;

                    try
                    {
                        // Read first message out of the incoming data
                        if (!buffer.IsEmpty && TextMessageParser.TryParseMessage(ref buffer, out var payload))
                        {
                            // Buffer was advanced to the end of the message by TryParseMessage
                            consumed = buffer.Start;
                            var message = HandshakeProtocol.ParseResponseMessage(payload.ToArray());

                            if (!string.IsNullOrEmpty(message.Error))
                            {
                                Log.HandshakeServerError(_logger, message.Error);
                                throw new HubException(
                                    $"Unable to complete handshake with the server due to an error: {message.Error}");
                            }

                            break;
                        }
                        else if (result.IsCompleted)
                        {
                            // Not enough data, and we won't be getting any more data.
                            throw new InvalidOperationException(
                                "The server disconnected before sending a handshake response");
                        }
                    }
                    finally
                    {
                        _connectionState.Connection.Transport.Input.AdvanceTo(consumed);
                    }
                }
            }
            catch (Exception ex)
            {
                // shutdown if we're unable to read handshake
                Log.ErrorReceivingHandshakeResponse(_logger, ex);
                throw;
            }

            Log.HandshakeComplete(_logger);
        }

        private async Task ReceiveLoop(ConnectionState connectionState)
        {
            // We hold a local capture of the connection state because StopAsync may dump out the current one.
            // We'll be locking any time we want to check back in to the "active" connection state.

            Log.ReceiveLoopStarting(_logger);

            var timeoutTimer = StartTimeoutTimer(connectionState);

            try
            {
                while (true)
                {
                    var result = await connectionState.Connection.Transport.Input.ReadAsync();
                    var buffer = result.Buffer;
                    var consumed = buffer.End; // TODO: Support partial messages
                    var examined = buffer.End;

                    try
                    {
                        if (result.IsCanceled)
                        {
                            // We were cancelled. Possibly because we were stopped gracefully
                            break;
                        }
                        else if (!buffer.IsEmpty)
                        {
                            ResetTimeoutTimer(timeoutTimer);

                            // We have data, process it
                            var (close, exception) = await ProcessMessagesAsync(buffer, connectionState);
                            if (close)
                            {
                                // Closing because we got a close frame, possibly with an error in it.
                                connectionState.CloseException = exception;
                                break;
                            }
                        }
                        else if (result.IsCompleted)
                        {
                            break;
                        }
                    }
                    finally
                    {
                        connectionState.Connection.Transport.Input.AdvanceTo(consumed, examined);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.ServerDisconnectedWithError(_logger, ex);
                connectionState.CloseException = ex;
            }
            
            // Clear the connectionState field
            await WaitConnectionLockAsync();
            try
            {
                SafeAssert(ReferenceEquals(_connectionState, connectionState),
                    "Someone other than ReceiveLoop cleared the connection state!");
                _connectionState = null;
            }
            finally
            {
                ReleaseConnectionLock();
            }

            // Stop the timeout timer.
            timeoutTimer?.Dispose();

            // Dispose the connection
            await connectionState.Connection.DisposeAsync();

            // Cancel any outstanding invocations within the connection lock
            connectionState.CancelOutstandingInvocations(connectionState.CloseException);

            if (connectionState.CloseException != null)
            {
                Log.ShutdownWithError(_logger, connectionState.CloseException);
            }
            else
            {
                Log.ShutdownConnection(_logger);
            }

            // Fire-and-forget the closed event
            RunClosedEvent(connectionState.CloseException);
        }

        private void RunClosedEvent(Exception closeException)
        {
            _ = Task.Run(() =>
            {
                try
                {
                    Log.InvokingClosedEventHandler(_logger);
                    Closed?.Invoke(closeException);
                }
                catch (Exception ex)
                {
                    Log.ErrorDuringClosedEvent(_logger, ex);
                }
            });
        }

        private void ResetTimeoutTimer(Timer timeoutTimer)
        {
            if (timeoutTimer != null)
            {
                Log.ResettingKeepAliveTimer(_logger);
                timeoutTimer.Change(ServerTimeout, Timeout.InfiniteTimeSpan);
            }
        }

        private Timer StartTimeoutTimer(ConnectionState connectionState)
        {
            // Check if we need keep-alive
            Timer timeoutTimer = null;
            if (connectionState.Connection.Features.Get<IConnectionInherentKeepAliveFeature>() == null)
            {
                Log.StartingServerTimeoutTimer(_logger, ServerTimeout);
                timeoutTimer = new Timer(
                    state => OnTimeout((ConnectionState)state),
                    connectionState,
                    dueTime: ServerTimeout,
                    period: Timeout.InfiniteTimeSpan);
            }
            else
            {
                Log.NotUsingServerTimeout(_logger);
            }

            return timeoutTimer;
        }

        private void OnTimeout(ConnectionState connectionState)
        {
            if (!Debugger.IsAttached)
            {
                connectionState.CloseException = new TimeoutException(
                    $"Server timeout ({ServerTimeout.TotalMilliseconds:0.00}ms) elapsed without receiving a message from the server.");
                connectionState.Connection.Transport.Input.CancelPendingRead();
            }
        }

        private ValueTask<FlushResult> WriteAsync(byte[] payload, CancellationToken cancellationToken = default)
        {
            AssertConnectionValid();
            return _connectionState.Connection.Transport.Output.WriteAsync(payload, cancellationToken);
        }

        private void CheckConnectionActive(string methodName)
        {
            if (_connectionState == null || _connectionState.Stopping)
            {
                throw new InvalidOperationException($"The '{methodName}' method cannot be called if the connection is not active");
            }
        }

        // Debug.Assert plays havoc with Unit Tests. But I want something that I can "assert" only in Debug builds.
        [Conditional("DEBUG")]
        private static void SafeAssert(bool condition, string message, [CallerMemberName] string memberName = null, [CallerFilePath] string fileName = null, [CallerLineNumber] int lineNumber = 0)
        {
            if (!condition)
            {
                throw new Exception($"Assertion failed in {memberName}, at {fileName}:{lineNumber}: {message}");
            }
        }

        [Conditional("DEBUG")]
        private void AssertInConnectionLock([CallerMemberName] string memberName = null, [CallerFilePath] string fileName = null, [CallerLineNumber] int lineNumber = 0) => SafeAssert(_connectionLock.CurrentCount == 0, "We're not in the Connection Lock!", memberName, fileName, lineNumber);

        [Conditional("DEBUG")]
        private void AssertConnectionValid([CallerMemberName] string memberName = null, [CallerFilePath] string fileName = null, [CallerLineNumber] int lineNumber = 0)
        {
            AssertInConnectionLock(memberName, fileName, lineNumber);
            SafeAssert(_connectionState != null, "We don't have a connection!", memberName, fileName, lineNumber);
        }

        private Task WaitConnectionLockAsync([CallerMemberName] string memberName = null, [CallerFilePath] string filePath = null, [CallerLineNumber] int lineNumber = 0)
        {
            Log.WaitingOnConnectionLock(_logger, memberName, filePath, lineNumber);
            return _connectionLock.WaitAsync();
        }

        private void ReleaseConnectionLock([CallerMemberName] string memberName = null,
            [CallerFilePath] string filePath = null, [CallerLineNumber] int lineNumber = 0)
        {
            Log.ReleasingConnectionLock(_logger, memberName, filePath, lineNumber);
            _connectionLock.Release();
        }

        private class Subscription : IDisposable
        {
            private readonly InvocationHandler _handler;
            private readonly List<InvocationHandler> _handlerList;

            public Subscription(InvocationHandler handler, List<InvocationHandler> handlerList)
            {
                _handler = handler;
                _handlerList = handlerList;
            }

            public void Dispose()
            {
                lock (_handlerList)
                {
                    _handlerList.Remove(_handler);
                }
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

        // Represents all the transient state about a connection
        // This includes binding information because return type binding depends upon _pendingCalls
        private class ConnectionState : IInvocationBinder
        {
            private volatile bool _stopping;
            private readonly HubConnection _hubConnection;

            private TaskCompletionSource<object> _stopTcs;
            private readonly object _lock = new object();
            private readonly Dictionary<string, InvocationRequest> _pendingCalls = new Dictionary<string, InvocationRequest>();
            private int _nextId;

            public IConnection Connection { get; }
            public Task ReceiveTask { get; set; }
            public Exception CloseException { get; set; }

            public bool Stopping
            {
                get => _stopping;
                set => _stopping = value;
            }

            public ConnectionState(IConnection connection, HubConnection hubConnection)
            {
                _hubConnection = hubConnection;
                Connection = connection;
            }

            public string GetNextId() => Interlocked.Increment(ref _nextId).ToString();

            public void AddInvocation(InvocationRequest irq)
            {
                lock (_lock)
                {
                    if (_pendingCalls.ContainsKey(irq.InvocationId))
                    {
                        Log.InvocationAlreadyInUse(_hubConnection._logger, irq.InvocationId);
                        throw new InvalidOperationException($"Invocation ID '{irq.InvocationId}' is already in use.");
                    }
                    else
                    {
                        _pendingCalls.Add(irq.InvocationId, irq);
                    }
                }
            }

            public bool TryGetInvocation(string invocationId, out InvocationRequest irq)
            {
                lock (_lock)
                {
                    return _pendingCalls.TryGetValue(invocationId, out irq);
                }
            }

            public bool TryRemoveInvocation(string invocationId, out InvocationRequest irq)
            {
                lock (_lock)
                {
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

            public void CancelOutstandingInvocations(Exception exception)
            {
                Log.CancelingOutstandingInvocations(_hubConnection._logger);

                lock (_lock)
                {
                    foreach (var outstandingCall in _pendingCalls.Values)
                    {
                        Log.RemovingInvocation(_hubConnection._logger, outstandingCall.InvocationId);
                        if (exception != null)
                        {
                            outstandingCall.Fail(exception);
                        }
                        outstandingCall.Dispose();
                    }
                    _pendingCalls.Clear();
                }
            }

            public Task StopAsync(TimeSpan timeout)
            {
                // We want multiple StopAsync calls on the same connection state
                // to wait for the same "stop" to complete.
                lock (_lock)
                {
                    if (_stopTcs != null)
                    {
                        return _stopTcs.Task;
                    }
                    else
                    {
                        _stopTcs = new TaskCompletionSource<object>();
                        return StopAsyncCore(timeout);
                    }
                }
            }

            private async Task StopAsyncCore(TimeSpan timeout)
            {
                Log.Stopping(_hubConnection._logger);

                // Complete our write pipe, which should cause everything to shut down
                Log.TerminatingReceiveLoop(_hubConnection._logger);
                Connection.Transport.Input.CancelPendingRead();

                // Wait ServerTimeout for the server or transport to shut down.
                Log.WaitingForReceiveLoopToTerminate(_hubConnection._logger);
                await ReceiveTask;

                Log.Stopped(_hubConnection._logger);
                _stopTcs.TrySetResult(null);
            }

            Type IInvocationBinder.GetReturnType(string invocationId)
            {
                if (!TryGetInvocation(invocationId, out var irq))
                {
                    Log.ReceivedUnexpectedResponse(_hubConnection._logger, invocationId);
                    return null;
                }
                return irq.ResultType;
            }

            IReadOnlyList<Type> IInvocationBinder.GetParameterTypes(string methodName)
            {
                if (!_hubConnection._handlers.TryGetValue(methodName, out var handlers))
                {
                    Log.MissingHandler(_hubConnection._logger, methodName);
                    return Type.EmptyTypes;
                }

                // We use the parameter types of the first handler
                lock (handlers)
                {
                    if (handlers.Count > 0)
                    {
                        return handlers[0].ParameterTypes;
                    }
                    throw new InvalidOperationException($"There are no callbacks registered for the method '{methodName}'");
                }
            }
        }
    }
}
