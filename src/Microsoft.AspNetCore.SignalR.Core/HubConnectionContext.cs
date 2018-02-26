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

        public HubConnectionContext(ConnectionContext connectionContext, TimeSpan keepAliveInterval, ILoggerFactory loggerFactory)
        {
            Output = Channel.CreateUnbounded<byte[]>();
            _connectionContext = connectionContext;
            _logger = loggerFactory.CreateLogger<HubConnectionContext>();
            ConnectionAbortedToken = _connectionAbortedTokenSource.Token;
            _keepAliveDuration = (int)keepAliveInterval.TotalMilliseconds * (Stopwatch.Frequency / 1000);

            if (Features.Get<IConnectionInherentKeepAliveFeature>() == null)
            {
                Features.Get<IConnectionHeartbeatFeature>()?.OnHeartbeat(state => ((HubConnectionContext)state).KeepAliveTick(), this);
            }
        }

        public virtual CancellationToken ConnectionAbortedToken { get; }

        public virtual string ConnectionId => _connectionContext.ConnectionId;

        public virtual ClaimsPrincipal User => Features.Get<IConnectionUserFeature>()?.User;

        public virtual IFeatureCollection Features => _connectionContext.Features;

        public virtual IDictionary<object, object> Metadata => Features.Get<IConnectionMetadataFeature>().Metadata;

        public virtual PipeReader Input => _connectionContext.Transport.Input;

        public string UserIdentifier { get; private set; }

        internal virtual Channel<byte[]> Output { get; set; }

        internal ExceptionDispatchInfo AbortException { get; private set; }

        // Currently used only for streaming methods
        internal ConcurrentDictionary<string, CancellationTokenSource> ActiveRequestCancellationSources { get; } = new ConcurrentDictionary<string, CancellationTokenSource>();

        public IPAddress RemoteIpAddress => Features.Get<IHttpConnectionFeature>()?.RemoteIpAddress;

        public IPAddress LocalIpAddress => Features.Get<IHttpConnectionFeature>()?.LocalIpAddress;

        public int? RemotePort => Features.Get<IHttpConnectionFeature>()?.RemotePort;

        public int? LocalPort => Features.Get<IHttpConnectionFeature>()?.LocalPort;

        public async Task WriteAsync(byte[] message)
        {
            try
            {
                await _writeLock.WaitAsync();

                //var buffer = ProtocolReaderWriter.WriteMessage(message);

                _connectionContext.Transport.Output.Write(message.GetMessage(ProtocolReaderWriter));

                Interlocked.Exchange(ref _lastSendTimestamp, Stopwatch.GetTimestamp());

                await _connectionContext.Transport.Output.FlushAsync(CancellationToken.None);
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

        internal async Task<bool> NegotiateAsync(TimeSpan timeout, IHubProtocolResolver protocolResolver, IUserIdProvider userIdProvider)
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
                                    var protocol = protocolResolver.GetProtocol(negotiationMessage.Protocol, this);

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

                                    Log.UsingHubProtocol(_logger, protocol.Name);

                                    UserIdentifier = userIdProvider.GetUserId(this);

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

        private async Task StartAsyncCore()
        {
            if (Features.Get<IConnectionInherentKeepAliveFeature>() == null)
            {
                Debug.Assert(ProtocolReaderWriter != null, "Expected the ProtocolReaderWriter to be set before StartAsync is called");
                _pingMessage = ProtocolReaderWriter.WriteMessage(PingMessage.Instance);
                _connectionContext.Features.Get<IConnectionHeartbeatFeature>()?.OnHeartbeat(state => ((HubConnectionContext)state).KeepAliveTick(), this);
            }

            try
            {
                while (await Output.Reader.WaitToReadAsync())
                {
                    while (Output.Reader.TryRead(out var hubMessage))
                    {
                        //var buffer = ProtocolReaderWriter.WriteMessage(hubMessage);
                        while (await _connectionContext.Transport.Writer.WaitToWriteAsync())
                        {
                            if (_connectionContext.Transport.Writer.TryWrite(hubMessage))
                            {
                                Interlocked.Exchange(ref _lastSendTimestamp, Stopwatch.GetTimestamp());
                                break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Abort(ex);
            }
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

                Log.SentPing(_logger);

                _ = WriteAsync(new CachedHubMessage(PingMessage.Instance));

                Interlocked.Exchange(ref _lastSendTimestamp, Stopwatch.GetTimestamp());
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

    public class CachedHubMessage
    {
        private readonly HubMessage _hubMessage;

        public CachedHubMessage(HubMessage hubMessage)
        {
            _hubMessage = hubMessage;
        }

        public byte[] GetMessage(HubProtocolReaderWriter protocolReaderWriter)
        {
            return protocolReaderWriter.WriteMessage(_hubMessage);
        }
    }
}
