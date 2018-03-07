// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net;
using System.Runtime.ExceptionServices;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Protocols;
using Microsoft.AspNetCore.SignalR.Core;
using Microsoft.AspNetCore.SignalR.Internal;
using Microsoft.AspNetCore.SignalR.Internal.Encoders;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Microsoft.AspNetCore.Sockets;
using Microsoft.AspNetCore.Sockets.Features;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.SignalR
{
    public class HubConnectionContext
    {
        private static Action<object> _abortedCallback = AbortConnection;
        private static readonly Base64Encoder Base64Encoder = new Base64Encoder();
        private static readonly PassThroughEncoder PassThroughEncoder = new PassThroughEncoder();

        private readonly ConnectionContext _connectionContext;
        private readonly ILogger _logger;
        private readonly CancellationTokenSource _connectionAbortedTokenSource = new CancellationTokenSource();
        private readonly TaskCompletionSource<object> _abortCompletedTcs = new TaskCompletionSource<object>();
        private readonly long _keepAliveDuration;
        private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1);

        private long _lastSendTimestamp = Stopwatch.GetTimestamp();
        private byte[] _cachedPingMessage;

        public HubConnectionContext(ConnectionContext connectionContext, TimeSpan keepAliveInterval, ILoggerFactory loggerFactory)
        {
            _connectionContext = connectionContext;
            _logger = loggerFactory.CreateLogger<HubConnectionContext>();
            ConnectionAbortedToken = _connectionAbortedTokenSource.Token;
            _keepAliveDuration = (int)keepAliveInterval.TotalMilliseconds * (Stopwatch.Frequency / 1000);
        }

        public virtual CancellationToken ConnectionAbortedToken { get; }

        public virtual string ConnectionId => _connectionContext.ConnectionId;

        public virtual ClaimsPrincipal User => Features.Get<IConnectionUserFeature>()?.User;

        public virtual IFeatureCollection Features => _connectionContext.Features;

        public virtual IDictionary<object, object> Metadata => Features.Get<IConnectionMetadataFeature>().Metadata;

        public virtual PipeReader Input => _connectionContext.Transport.Input;

        public string UserIdentifier { get; private set; }

        internal virtual HubProtocolReaderWriter ProtocolReaderWriter { get; set; }

        internal ExceptionDispatchInfo AbortException { get; private set; }

        // Currently used only for streaming methods
        internal ConcurrentDictionary<string, CancellationTokenSource> ActiveRequestCancellationSources { get; } = new ConcurrentDictionary<string, CancellationTokenSource>();

        public IPAddress RemoteIpAddress => Features.Get<IHttpConnectionFeature>()?.RemoteIpAddress;

        public IPAddress LocalIpAddress => Features.Get<IHttpConnectionFeature>()?.LocalIpAddress;

        public int? RemotePort => Features.Get<IHttpConnectionFeature>()?.RemotePort;

        public int? LocalPort => Features.Get<IHttpConnectionFeature>()?.LocalPort;

        public virtual async Task WriteAsync(HubMessage message)
        {
            await _writeLock.WaitAsync();

            try
            {
                // This will internally cache the buffer for each unique HubProtocol/DataEncoder combination
                // So that we don't serialize the HubMessage for every single connection
                var buffer = message.WriteMessage(ProtocolReaderWriter);
                _connectionContext.Transport.Output.Write(buffer);

                Interlocked.Exchange(ref _lastSendTimestamp, Stopwatch.GetTimestamp());

                await _connectionContext.Transport.Output.FlushAsync();
            }
            finally
            {
                _writeLock.Release();
            }
        }

        private async Task TryWritePingAsync()
        {
            // Don't wait for the lock, if it returns false that means someone wrote to the connection
            // and we don't need to send a ping anymore
            if (!await _writeLock.WaitAsync(0))
            {
                return;
            }

            try
            {
                Debug.Assert(_cachedPingMessage != null);
                _connectionContext.Transport.Output.Write(_cachedPingMessage);

                Interlocked.Exchange(ref _lastSendTimestamp, Stopwatch.GetTimestamp());

                await _connectionContext.Transport.Output.FlushAsync();
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public virtual void Abort()
        {
            // If we already triggered the token then noop, this isn't thread safe but it's good enough
            // to avoid spawning a new task in the most common cases
            if (_connectionAbortedTokenSource.IsCancellationRequested)
            {
                return;
            }

            // We fire and forget since this can trigger user code to run
            Task.Factory.StartNew(_abortedCallback, this);
        }

        internal async Task<bool> NegotiateAsync(TimeSpan timeout, IList<string> supportedProtocols, IHubProtocolResolver protocolResolver, IUserIdProvider userIdProvider)
        {
            try
            {
                using (var cts = new CancellationTokenSource())
                {
                    cts.CancelAfter(timeout);

                    while (true)
                    {
                        var result = await _connectionContext.Transport.Input.ReadAsync(cts.Token);
                        var buffer = result.Buffer;
                        var consumed = buffer.End;
                        var examined = buffer.End;

                        try
                        {
                            if (!buffer.IsEmpty)
                            {
                                if (NegotiationProtocol.TryParseMessage(buffer, out var negotiationMessage, out consumed, out examined))
                                {
                                    var protocol = protocolResolver.GetProtocol(negotiationMessage.Protocol, supportedProtocols, this);

                                    var transportCapabilities = Features.Get<IConnectionTransportFeature>()?.TransportCapabilities
                                        ?? throw new InvalidOperationException("Unable to read transport capabilities.");

                                    var dataEncoder = (protocol.Type == ProtocolType.Binary && (transportCapabilities & TransferMode.Binary) == 0)
                                        ? (IDataEncoder)Base64Encoder
                                        : PassThroughEncoder;

                                    var transferModeFeature = Features.Get<ITransferModeFeature>() ??
                                        throw new InvalidOperationException("Unable to read transfer mode.");

                                    transferModeFeature.TransferMode =
                                        (protocol.Type == ProtocolType.Binary && (transportCapabilities & TransferMode.Binary) != 0)
                                            ? TransferMode.Binary
                                            : TransferMode.Text;

                                    ProtocolReaderWriter = new HubProtocolReaderWriter(protocol, dataEncoder);
                                    _cachedPingMessage = ProtocolReaderWriter.WriteMessage(PingMessage.Instance);

                                    Log.UsingHubProtocol(_logger, protocol.Name);

                                    UserIdentifier = userIdProvider.GetUserId(this);

                                    if (Features.Get<IConnectionInherentKeepAliveFeature>() == null)
                                    {
                                        // Only register KeepAlive after protocol negotiated otherwise KeepAliveTick could try to write without having a ProtocolReaderWriter
                                        Features.Get<IConnectionHeartbeatFeature>()?.OnHeartbeat(state => ((HubConnectionContext)state).KeepAliveTick(), this);
                                    }

                                    return true;
                                }
                            }
                            else if (result.IsCompleted)
                            {
                                break;
                            }
                        }
                        finally
                        {
                            _connectionContext.Transport.Input.AdvanceTo(consumed, examined);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Log.NegotiateCanceled(_logger);
            }

            return false;
        }

        internal void Abort(Exception exception)
        {
            AbortException = ExceptionDispatchInfo.Capture(exception);
            Abort();
        }

        // Used by the HubEndPoint only
        internal Task AbortAsync()
        {
            Abort();
            return _abortCompletedTcs.Task;
        }

        private void KeepAliveTick()
        {
            // Implements the keep-alive tick behavior
            // Each tick, we check if the time since the last send is larger than the keep alive duration (in ticks).
            // If it is, we send a ping frame, if not, we no-op on this tick. This means that in the worst case, the
            // true "ping rate" of the server could be (_hubOptions.KeepAliveInterval + HubEndPoint.KeepAliveTimerInterval),
            // because if the interval elapses right after the last tick of this timer, it won't be detected until the next tick.

            if (Stopwatch.GetTimestamp() - Interlocked.Read(ref _lastSendTimestamp) > _keepAliveDuration)
            {
                // Haven't sent a message for the entire keep-alive duration, so send a ping.
                // If the transport channel is full, this will fail, but that's OK because
                // adding a Ping message when the transport is full is unnecessary since the
                // transport is still in the process of sending frames.

                _ = TryWritePingAsync();
                Log.SentPing(_logger);
            }
        }

        private static void AbortConnection(object state)
        {
            var connection = (HubConnectionContext)state;
            try
            {
                connection._connectionAbortedTokenSource.Cancel();

                // Communicate the fact that we're finished triggering abort callbacks
                connection._abortCompletedTcs.TrySetResult(null);
            }
            catch (Exception ex)
            {
                // TODO: Should we log if the cancellation callback fails? This is more preventative to make sure
                // we don't end up with an unobserved task
                connection._abortCompletedTcs.TrySetException(ex);
            }
        }

        private static class Log
        {
            // Category: HubConnectionContext
            private static readonly Action<ILogger, string, Exception> _usingHubProtocol =
                LoggerMessage.Define<string>(LogLevel.Information, new EventId(1, "UsingHubProtocol"), "Using HubProtocol '{protocol}'.");

            private static readonly Action<ILogger, Exception> _negotiateCanceled =
                LoggerMessage.Define(LogLevel.Debug, new EventId(2, "NegotiateCanceled"), "Negotiate was canceled.");

            private static readonly Action<ILogger, Exception> _sentPing =
                LoggerMessage.Define(LogLevel.Trace, new EventId(3, "SentPing"), "Sent a ping message to the client.");

            private static readonly Action<ILogger, Exception> _transportBufferFull =
                LoggerMessage.Define(LogLevel.Debug, new EventId(4, "TransportBufferFull"), "Unable to send Ping message to client, the transport buffer is full.");

            public static void UsingHubProtocol(ILogger logger, string hubProtocol)
            {
                _usingHubProtocol(logger, hubProtocol, null);
            }

            public static void NegotiateCanceled(ILogger logger)
            {
                _negotiateCanceled(logger, null);
            }

            public static void SentPing(ILogger logger)
            {
                _sentPing(logger, null);
            }

            public static void TransportBufferFull(ILogger logger)
            {
                _transportBufferFull(logger, null);
            }
        }

    }
}
