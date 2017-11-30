// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Security.Claims;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR.Features;
using Microsoft.AspNetCore.SignalR.Internal;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Microsoft.AspNetCore.Sockets;
using Microsoft.AspNetCore.Sockets.Features;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.SignalR
{
    public class HubConnectionContext
    {
        private static Action<object> _abortedCallback = AbortConnection;

        private readonly Channel<HubMessage> _output;
        private readonly ConnectionContext _connectionContext;
        private readonly CancellationTokenSource _connectionAbortedTokenSource = new CancellationTokenSource();
        private readonly TaskCompletionSource<object> _abortCompletedTcs = new TaskCompletionSource<object>();
        private Task _writingTask = Task.CompletedTask;

        private long _lastSendTimestamp = Stopwatch.GetTimestamp();

        public HubConnectionContext(ConnectionContext connectionContext)
        {
            _output = Channel.CreateUnbounded<HubMessage>();
            _connectionContext = connectionContext;
            ConnectionAbortedToken = _connectionAbortedTokenSource.Token;
        }

        private IHubFeature HubFeature => Features.Get<IHubFeature>();

        // Used by the HubEndPoint only
        internal Channel<byte[]> Transport => _connectionContext.Transport;

        internal ExceptionDispatchInfo AbortException { get; private set; }

        internal byte[] PingMessage { get; set; }

        internal Timer KeepAliveTimer { get; set; }

        internal long KeepAliveDuration { get; set; }

        internal ILogger Logger { get; set; }

        internal bool RequiresKeepAlives => Features.Get<IConnectionInherentKeepAliveFeature>() == null;

        public virtual CancellationToken ConnectionAbortedToken { get; }

        public virtual string ConnectionId => _connectionContext.ConnectionId;

        public virtual ClaimsPrincipal User => Features.Get<IConnectionUserFeature>()?.User;

        public virtual IFeatureCollection Features => _connectionContext.Features;

        public virtual IDictionary<object, object> Metadata => _connectionContext.Metadata;

        public virtual HubProtocolReaderWriter ProtocolReaderWriter { get; set; }

        public virtual ChannelWriter<HubMessage> Output => _output;

        // Currently used only for streaming methods
        internal ConcurrentDictionary<string, CancellationTokenSource> ActiveRequestCancellationSources { get; } = new ConcurrentDictionary<string, CancellationTokenSource>();

        public string UserIdentifier { get; internal set; }

        // Hubs support multiple producers so we set up this loop to copy
        // data written to the HubConnectionContext's channel to the transport channel
        internal Task StartAsync()
        {
            return _writingTask = StartAsyncCore();
        }

        private async Task StartAsyncCore()
        {
            try
            {
                while (await _output.Reader.WaitToReadAsync())
                {
                    while (_output.Reader.TryRead(out var hubMessage))
                    {
                        var buffer = ProtocolReaderWriter.WriteMessage(hubMessage);
                        while (await Transport.Writer.WaitToWriteAsync())
                        {
                            if (Transport.Writer.TryWrite(buffer))
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

        internal void StartKeepAliveTimer(TimeSpan keepAliveTimerInterval)
        {
            KeepAliveTimer?.Dispose();
            KeepAliveTimer = new Timer(KeepAliveTick, this, keepAliveTimerInterval, keepAliveTimerInterval);
        }

        private static void KeepAliveTick(object state)
        {
            var connectionContext = (HubConnectionContext)state;
            var lastSendTimestamp = connectionContext._lastSendTimestamp;

            if (Stopwatch.GetTimestamp() - Interlocked.Read(ref lastSendTimestamp) > connectionContext.KeepAliveDuration)
            {
                // Haven't sent a message for the entire keep-alive duration, so send a ping.
                // If the transport channel is full, this will fail, but that's OK because
                // adding a Ping message when the transport is full is unnecessary since the
                // transport is still in the process of sending frames.
                if (connectionContext.Transport.Writer.TryWrite(connectionContext.PingMessage))
                {
                    connectionContext.Logger.LogTrace("Sent Ping fame to client");
                }
                else
                {
                    // This isn't necessarily an error, it just indicates that the transport is applying backpressure right now.
                    connectionContext.Logger.LogDebug("Unable to send Ping message to client, the transport buffer is full.");
                }

                Interlocked.Exchange(ref lastSendTimestamp, Stopwatch.GetTimestamp());
            }
        }

        public async Task DisposeAsync()
        {
            KeepAliveTimer?.Dispose();

            // Nothing should be writing to the HubConnectionContext
            _output.Writer.TryComplete();

            // This should unwind once we complete the output
            await _writingTask;
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
    }
}
