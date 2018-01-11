// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Sockets.Client.Internal;
using Microsoft.AspNetCore.Sockets.Features;
using Microsoft.AspNetCore.Sockets.Http.Internal;
using Microsoft.AspNetCore.Sockets.Internal;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Microsoft.AspNetCore.Sockets.Client.Http.Internal
{
    internal class HttpConnectionSession
    {
        private readonly ILogger _logger;

        private volatile ConnectionState _connectionState = ConnectionState.Disconnected;
        private readonly object _stateChangeLock = new object();

        private readonly HttpClient _httpClient;
        private readonly HttpOptions _httpOptions;
        private readonly ITransportFactory _transportFactory;
        private readonly TimeSpan _eventQueueDrainTimeout = TimeSpan.FromSeconds(5);
        private readonly TransportType _requestedTransportType;
        private readonly IConnection _connection;
        private readonly Uri _url;

        private volatile ChannelConnection<byte[], SendMessage> _transportChannel;
        private volatile ITransport _transport;
        private volatile Task _receiveLoopTask;
        private TaskCompletionSource<object> _startTcs;
        private TaskCompletionSource<object> _closeTcs;
        private TaskQueue _eventQueue;
        private string _connectionId;
        private Exception _abortException;
        private ChannelReader<byte[]> Input => _transportChannel.Input;
        private ChannelWriter<SendMessage> Output => _transportChannel.Output;

        public IFeatureCollection Features => _connection.Features;

        private Action<Exception> _closedEvent;

        private Func<byte[], Task> _onReceiveCallback;

        public HttpConnectionSession(Uri url, ITransportFactory transportFactory, TransportType transportType, ILogger logger, HttpClient httpClient,
            HttpOptions httpOptions, IConnection connection, Func<byte[], Task> onReceivedCallback, Action<Exception> closedEvent)
        {
            _url = url ?? throw new ArgumentNullException(nameof(url));
            _logger = logger;
            _httpClient = httpClient;
            _httpOptions = httpOptions;
            _requestedTransportType = transportType;
            _transportFactory = transportFactory ?? throw new ArgumentNullException(nameof(transportFactory));
            _connection = connection;
            _onReceiveCallback = onReceivedCallback;
            _closedEvent = closedEvent;
        }

        public Task StartAsync()
        {
            if (ChangeState(from: ConnectionState.Disconnected, to: ConnectionState.Connecting) != ConnectionState.Disconnected)
            {
                return Task.FromException(
                    new InvalidOperationException($"Cannot start a connection that is not in the {nameof(ConnectionState.Disconnected)} state."));
            }

            _startTcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            _eventQueue = new TaskQueue();

            StartAsyncInternal()
                .ContinueWith(t =>
                {
                    var abortException = _abortException;
                    if (t.IsFaulted || abortException != null)
                    {
                        _startTcs.SetException(_abortException ?? t.Exception.InnerException);
                    }
                    else if (t.IsCanceled)
                    {
                        _startTcs.SetCanceled();
                    }
                    else
                    {
                        _startTcs.SetResult(null);
                    }
                });

            return _startTcs.Task;
        }

        private async Task StartAsyncInternal()
        {
            _logger.HttpConnectionStarting();

            try
            {
                var connectUrl = _url;
                if (_requestedTransportType == TransportType.WebSockets)
                {
                    _transport = _transportFactory.CreateTransport(TransportType.WebSockets);
                }
                else
                {
                    var negotiationResponse = await Negotiate(_url, _httpClient, _logger);
                    _connectionId = negotiationResponse.ConnectionId;

                    // Connection is being disposed while start was in progress
                    if (_connectionState == ConnectionState.Disposed)
                    {
                        _logger.HttpConnectionClosed(_connectionId);
                        return;
                    }

                    _transport = _transportFactory.CreateTransport(GetAvailableServerTransports(negotiationResponse));
                    connectUrl = CreateConnectUrl(_url, negotiationResponse);
                }

                _logger.StartingTransport(_connectionId, _transport.GetType().Name, connectUrl);
                await StartTransport(connectUrl);
            }
            catch
            {
                // The connection can now be either in the Connecting or Disposed state - only change the state to
                // Disconnected if the connection was in the Connecting state to not resurrect a Disposed connection
                ChangeState(from: ConnectionState.Connecting, to: ConnectionState.Disconnected);
                throw;
            }

            // if the connection is not in the Connecting state here it means the user called DisposeAsync while
            // the connection was starting
            if (ChangeState(from: ConnectionState.Connecting, to: ConnectionState.Connected) == ConnectionState.Connecting)
            {
                _closeTcs = new TaskCompletionSource<object>();

                _ = Input.Completion.ContinueWith(async t =>
                {
                    // Grab the exception and then clear it.
                    // See comment at AbortAsync for more discussion on the thread-safety
                    var abortException = _abortException;
                    _abortException = null;

                    // There is an inherent race between receive and close. Removing the last message from the channel
                    // makes Input.Completion task completed and runs this continuation. We need to await _receiveLoopTask
                    // to make sure that the message removed from the channel is processed before we drain the queue.
                    // There is a short window between we start the channel and assign the _receiveLoopTask a value.
                    // To make sure that _receiveLoopTask can be awaited (i.e. is not null) we need to await _startTask.
                    _logger.ProcessRemainingMessages(_connectionId);

                    await _startTcs.Task;
                    await _receiveLoopTask;

                    _logger.DrainEvents(_connectionId);

                    await Task.WhenAny(_eventQueue.Drain().NoThrow(), Task.Delay(_eventQueueDrainTimeout));

                    _logger.CompleteClosed(_connectionId);

                    // At this point the connection can be either in the Connected or Disposed state. The state should be changed
                    // to the Disconnected state only if it was in the Connected state.
                    ChangeState(from: ConnectionState.Connected, to: ConnectionState.Disconnected);

                    try
                    {
                        if (t.IsFaulted)
                        {
                            _closedEvent.Invoke(t.Exception.InnerException);
                        }
                        else
                        {
                            // Call the closed event. If there was an abort exception, it will be flowed forward
                            // However, if there wasn't, this will just be null and we're good
                            _closedEvent.Invoke(abortException);
                        }
                    }
                    catch (Exception ex)
                    {
                        // Suppress (but log) the exception, this is user code
                        _logger.ErrorDuringClosedEvent(ex);
                    }

                    _closeTcs.SetResult(null);
                });

                _receiveLoopTask = ReceiveAsync();
            }
        }

        private async Task<NegotiationResponse> Negotiate(Uri url, HttpClient httpClient, ILogger logger)
        {
            try
            {
                // Get a connection ID from the server
                logger.EstablishingConnection(url);
                var urlBuilder = new UriBuilder(url);
                if (!urlBuilder.Path.EndsWith("/"))
                {
                    urlBuilder.Path += "/";
                }
                urlBuilder.Path += "negotiate";

                using (var request = new HttpRequestMessage(HttpMethod.Post, urlBuilder.Uri))
                {
                    SendUtils.PrepareHttpRequest(request, _httpOptions);

                    using (var response = await httpClient.SendAsync(request))
                    {
                        response.EnsureSuccessStatusCode();
                        return await ParseNegotiateResponse(response, logger);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.ErrorWithNegotiation(url, ex);
                throw;
            }
        }

        private static async Task<NegotiationResponse> ParseNegotiateResponse(HttpResponseMessage response, ILogger logger)
        {
            NegotiationResponse negotiationResponse;
            using (var reader = new JsonTextReader(new StreamReader(await response.Content.ReadAsStreamAsync())))
            {
                try
                {
                    negotiationResponse = new JsonSerializer().Deserialize<NegotiationResponse>(reader);
                }
                catch (Exception ex)
                {
                    throw new FormatException("Invalid negotiation response received.", ex);
                }
            }

            if (negotiationResponse == null)
            {
                throw new FormatException("Invalid negotiation response received.");
            }

            return negotiationResponse;
        }

        private TransportType GetAvailableServerTransports(NegotiationResponse negotiationResponse)
        {
            if (negotiationResponse.AvailableTransports == null)
            {
                throw new FormatException("No transports returned in negotiation response.");
            }

            var availableServerTransports = (TransportType)0;
            foreach (var t in negotiationResponse.AvailableTransports)
            {
                availableServerTransports |= t;
            }

            return availableServerTransports;
        }

        private static Uri CreateConnectUrl(Uri url, NegotiationResponse negotiationResponse)
        {
            if (string.IsNullOrWhiteSpace(negotiationResponse.ConnectionId))
            {
                throw new FormatException("Invalid connection id returned in negotiation response.");
            }

            return Utils.AppendQueryString(url, "id=" + negotiationResponse.ConnectionId);
        }

        private async Task StartTransport(Uri connectUrl)
        {
            var applicationToTransport = Channel.CreateUnbounded<SendMessage>();
            var transportToApplication = Channel.CreateUnbounded<byte[]>();
            var applicationSide = ChannelConnection.Create(applicationToTransport, transportToApplication);
            _transportChannel = ChannelConnection.Create(transportToApplication, applicationToTransport);

            // Start the transport, giving it one end of the pipeline
            try
            {
                await _transport.StartAsync(connectUrl, applicationSide, GetTransferMode(), _connectionId, _connection);

                // actual transfer mode can differ from the one that was requested so set it on the feature
                if (!_transport.Mode.HasValue)
                {
                    // This can happen with custom transports so it should be an exception, not an assert.
                    throw new InvalidOperationException("Transport was expected to set the Mode property after StartAsync, but it has not been set.");
                }
                SetTransferMode(_transport.Mode.Value);
            }
            catch (Exception ex)
            {
                _logger.ErrorStartingTransport(_connectionId, _transport.GetType().Name, ex);
                throw;
            }
        }

        private TransferMode GetTransferMode()
        {
            var transferModeFeature = Features.Get<ITransferModeFeature>();
            if (transferModeFeature == null)
            {
                return TransferMode.Text;
            }

            return transferModeFeature.TransferMode;
        }

        private void SetTransferMode(TransferMode transferMode)
        {
            var transferModeFeature = Features.Get<ITransferModeFeature>();
            if (transferModeFeature == null)
            {
                transferModeFeature = new TransferModeFeature();
                Features.Set(transferModeFeature);
            }

            transferModeFeature.TransferMode = transferMode;
        }

        private async Task ReceiveAsync()
        {
            try
            {
                _logger.HttpReceiveStarted(_connectionId);

                while (await Input.WaitToReadAsync())
                {
                    if (_connectionState != ConnectionState.Connected)
                    {
                        _logger.SkipRaisingReceiveEvent(_connectionId);
                        // drain
                        Input.TryRead(out _);
                        continue;
                    }

                    if (Input.TryRead(out var buffer))
                    {
                        _logger.ScheduleReceiveEvent(_connectionId);
                        _ = _eventQueue.Enqueue(() =>
                        {
                            _logger.RaiseReceiveEvent(_connectionId);

                            return _onReceiveCallback(buffer);
                        });
                    }
                    else
                    {
                        _logger.FailedReadingMessage(_connectionId);
                    }
                }

                await Input.Completion;
            }
            catch (Exception ex)
            {
                Output.TryComplete(ex);
                _logger.ErrorReceiving(_connectionId, ex);
            }

            _logger.EndReceive(_connectionId);
        }

        public async Task SendAsync(byte[] data, CancellationToken cancellationToken = default)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            if (_connectionState != ConnectionState.Connected)
            {
                throw new InvalidOperationException(
                    "Cannot send messages when the connection is not in the Connected state.");
            }

            // TaskCreationOptions.RunContinuationsAsynchronously ensures that continuations awaiting
            // SendAsync (i.e. user's code) are not running on the same thread as the code that sets
            // TaskCompletionSource result. This way we prevent from user's code blocking our channel
            // send loop.
            var sendTcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            var message = new SendMessage(data, sendTcs);

            _logger.SendingMessage(_connectionId);

            while (await Output.WaitToWriteAsync(cancellationToken))
            {
                if (Output.TryWrite(message))
                {
                    await sendTcs.Task;
                    break;
                }
            }
        }

        public async Task StopAsync(Exception exception)
        {
            lock (_stateChangeLock)
            {
                if (!(_connectionState == ConnectionState.Connecting || _connectionState == ConnectionState.Connected))
                {
                    _logger.SkippingStop(_connectionId);
                    return;
                }
            }

            // Note that this method can be called at the same time when the connection is being closed from the server
            // side due to an error. We are resilient to this since we merely try to close the channel here and the
            // channel can be closed only once. As a result the continuation that does actual job and raises the Closed
            // event runs always only once.

            // See comment at AbortAsync for more discussion on the thread-safety of this.
            _abortException = exception;

            _logger.StoppingClient(_connectionId);

            try
            {
                await _startTcs.Task;
            }
            catch
            {
                // We only await the start task to make sure that StartAsync completed. The
                // _startTask is returned to the user and they should handle exceptions.
            }

            if (_transportChannel != null)
            {
                Output.TryComplete();
            }

            if (_transport != null)
            {
                await _transport.StopAsync();
            }

            if (_receiveLoopTask != null)
            {
                await _receiveLoopTask;
            }

            if (_closeTcs != null)
            {
                await _closeTcs.Task;
            }
        }

        public async Task DisposeAsync()
        {
            // This will no-op if we're already stopped
            await StopAsync(exception: null);

            if (ChangeState(to: ConnectionState.Disposed) == ConnectionState.Disposed)
            {
                _logger.SkippingDispose(_connectionId);

                // the connection was already disposed
                return;
            }

            _logger.DisposingClient(_connectionId);
        }

        private ConnectionState ChangeState(ConnectionState from, ConnectionState to)
        {
            lock (_stateChangeLock)
            {
                var state = _connectionState;
                if (_connectionState == from)
                {
                    _connectionState = to;
                }

                _logger.ConnectionStateChanged(_connectionId, state, to);
                return state;
            }
        }

        private ConnectionState ChangeState(ConnectionState to)
        {
            lock (_stateChangeLock)
            {
                var state = _connectionState;
                _connectionState = to;
                _logger.ConnectionStateChanged(_connectionId, state, to);
                return state;
            }
        }

        private class NegotiationResponse
        {
            public string ConnectionId { get; set; }
            public TransportType[] AvailableTransports { get; set; }
        }
    }

    // Internal because it's used by logging to avoid ToStringing prematurely.
    internal enum ConnectionState
    {
        Disconnected,
        Connecting,
        Connected,
        Disposed
    }
}
