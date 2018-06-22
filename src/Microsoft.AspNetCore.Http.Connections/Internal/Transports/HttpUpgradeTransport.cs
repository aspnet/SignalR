// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipelines;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.WebSockets.Internal;

namespace Microsoft.AspNetCore.Http.Connections.Internal.Transports
{
    public partial class HttpUpgradeTransport : IHttpTransport
    {
        private readonly WebSocketOptions _options;
        private readonly ILogger _logger;
        private readonly IDuplexPipe _application;
        private readonly HttpConnectionContext _connection;
        private volatile bool _aborted;

        public HttpUpgradeTransport(WebSocketOptions options, IDuplexPipe application, HttpConnectionContext connection, ILoggerFactory loggerFactory)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (application == null)
            {
                throw new ArgumentNullException(nameof(application));
            }

            if (loggerFactory == null)
            {
                throw new ArgumentNullException(nameof(loggerFactory));
            }

            _options = options;
            _application = application;
            _connection = connection;
            _logger = loggerFactory.CreateLogger<HttpUpgradeTransport>();
        }

        public async Task ProcessRequestAsync(HttpContext context, CancellationToken token)
        {
            Debug.Assert(context.WebSockets.IsWebSocketRequest, "Not a websocket request");

            var subProtocol = _options.SubProtocolSelector?.Invoke(context.WebSockets.WebSocketRequestedProtocols);

            var upgradeFeature = context.Features.Get<IHttpUpgradeFeature>();

            // A bit allocatey but copied from the websocket middleware
            string key = string.Join(", ", context.Request.Headers[Constants.Headers.SecWebSocketKey]);

            HandshakeHelpers.GenerateResponseHeaders(key, subProtocol, context);

            var duplexStream = await upgradeFeature.UpgradeAsync();

            try
            {
                Log.SocketOpened(_logger, subProtocol);

                await ProcessSocketAsync(context, duplexStream);
            }
            finally
            {
                Log.SocketClosed(_logger);
            }
        }

        public async Task ProcessSocketAsync(HttpContext context, Stream socket)
        {
            // Begin sending and receiving. Receiving must be started first because ExecuteAsync enables SendAsync.
            var receiving = StartReceiving(socket);
            var sending = StartSending(socket);

            // Wait for send or receive to complete
            var trigger = await Task.WhenAny(receiving, sending);

            if (trigger == receiving)
            {
                Log.WaitingForSend(_logger);

                // We're waiting for the application to finish and there are 2 things it could be doing
                // 1. Waiting for application data
                // 2. Waiting for a websocket send to complete

                // Cancel the application so that ReadAsync yields
                _application.Input.CancelPendingRead();

                using (var delayCts = new CancellationTokenSource())
                {
                    var resultTask = await Task.WhenAny(sending, Task.Delay(_options.CloseTimeout, delayCts.Token));

                    if (resultTask != sending)
                    {
                        // We timed out so now we're in ungraceful shutdown mode
                        Log.CloseTimedOut(_logger);

                        // Abort the websocket if we're stuck in a pending send to the client
                        _aborted = true;

                        context.Abort();
                    }
                    else
                    {
                        delayCts.Cancel();
                    }
                }
            }
            else
            {
                Log.WaitingForClose(_logger);

                // We're waiting on the websocket to close and there are 2 things it could be doing
                // 1. Waiting for websocket data
                // 2. Waiting on a flush to complete (backpressure being applied)

                using (var delayCts = new CancellationTokenSource())
                {
                    var resultTask = await Task.WhenAny(receiving, Task.Delay(_options.CloseTimeout, delayCts.Token));

                    if (resultTask != receiving)
                    {
                        // Abort the websocket if we're stuck in a pending receive from the client
                        _aborted = true;

                        context.Abort();

                        // Cancel any pending flush so that we can quit
                        _application.Output.CancelPendingFlush();
                    }
                    else
                    {
                        delayCts.Cancel();
                    }
                }
            }
        }

        private async Task StartReceiving(Stream stream)
        {
            try
            {
                while (true)
                {
#if NETCOREAPP2_2
                    // Do a 0 byte read so that idle connections don't allocate a buffer when waiting for a read
                    await stream.ReadAsync(Memory<byte>.Empty, CancellationToken.None);
#endif
                    var memory = _application.Output.GetMemory();

#if NETCOREAPP2_2
                    var read = await stream.ReadAsync(memory, CancellationToken.None);
#else
                    var isArray = MemoryMarshal.TryGetArray<byte>(memory, out var arraySegment);
                    Debug.Assert(isArray);

                    // Exceptions are handled above where the send and receive tasks are being run.
                    var read = await stream.ReadAsync(arraySegment.Array, arraySegment.Offset, arraySegment.Count, CancellationToken.None);
#endif
                    // Need to check again for NetCoreApp2.2 because a close can happen between a 0-byte read and the actual read
                    if (read == 0)
                    {
                        return;
                    }

                    Log.MessageReceived(_logger, read);

                    _application.Output.Advance(read);

                    var flushResult = await _application.Output.FlushAsync();

                    // We canceled in the middle of applying back pressure
                    // or if the consumer is done
                    if (flushResult.IsCanceled || flushResult.IsCompleted)
                    {
                        break;
                    }
                }
            }
            catch (ConnectionResetException ex)
            {
                // Client has closed the WebSocket connection without completing the close handshake
                Log.ClosedPrematurely(_logger, ex);
            }
            catch (OperationCanceledException)
            {
                // Ignore aborts, don't treat them like transport errors
            }
            catch (Exception ex)
            {
                if (!_aborted)
                {
                    _application.Output.Complete(ex);

                    // We re-throw here so we can communicate that there was an error when sending
                    // the close frame
                    throw;
                }
            }
            finally
            {
                // We're done writing
                _application.Output.Complete();
            }
        }

        private async Task StartSending(Stream stream)
        {
            Exception error = null;

            try
            {
                while (true)
                {
                    var result = await _application.Input.ReadAsync();
                    var buffer = result.Buffer;

                    // Get a frame from the application

                    try
                    {
                        if (result.IsCanceled)
                        {
                            break;
                        }

                        if (!buffer.IsEmpty)
                        {
                            try
                            {
                                Log.SendPayload(_logger, buffer.Length);

                                await stream.WriteAsync(buffer);
                            }
                            catch (Exception ex)
                            {
                                if (!_aborted)
                                {
                                    Log.ErrorWritingFrame(_logger, ex);
                                }
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
                        _application.Input.AdvanceTo(buffer.End);
                    }
                }
            }
            catch (Exception ex)
            {
                error = ex;
            }
            finally
            {
                _application.Input.Complete();
            }
        }
    }
}
