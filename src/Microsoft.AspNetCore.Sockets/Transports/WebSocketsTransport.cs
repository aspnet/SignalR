﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebSockets.Internal;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.WebSockets.Internal;

namespace Microsoft.AspNetCore.Sockets.Transports
{
    public class WebSocketsTransport : IHttpTransport
    {
        private readonly WebSocketOptions _options;
        private static readonly WebSocketAcceptContext _emptyContext = new WebSocketAcceptContext();

        private WebSocketOpcode _lastOpcode = WebSocketOpcode.Continuation;
        private bool _lastFrameIncomplete = false;

        private readonly ILogger _logger;
        private readonly IChannelConnection<Message> _application;

        public WebSocketsTransport(WebSocketOptions options, IChannelConnection<Message> application, ILoggerFactory loggerFactory)
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
            _logger = loggerFactory.CreateLogger<WebSocketsTransport>();
        }

        public async Task ProcessRequestAsync(HttpContext context, CancellationToken token)
        {
            var feature = context.Features.Get<IHttpWebSocketConnectionFeature>();
            if (feature == null || !feature.IsWebSocketRequest)
            {
                _logger.LogWarning("Unable to handle WebSocket request, there is no WebSocket feature available.");
                return;
            }

            using (var ws = await feature.AcceptWebSocketConnectionAsync(_emptyContext))
            {
                _logger.LogInformation("Socket opened.");

                await ProcessSocketAsync(ws);
            }
            _logger.LogInformation("Socket closed.");
        }

        public async Task ProcessSocketAsync(IWebSocketConnection socket)
        {
            // Begin sending and receiving. Receiving must be started first because ExecuteAsync enables SendAsync.
            var receiving = socket.ExecuteAsync((frame, state) => ((WebSocketsTransport)state).HandleFrame(frame), this);
            var sending = StartSending(socket);

            // Wait for something to shut down.
            var trigger = await Task.WhenAny(
                receiving,
                sending);

            // What happened?
            if (trigger == receiving)
            {
                if (receiving.IsCanceled || receiving.IsFaulted)
                {
                    // The receiver faulted or cancelled. This means the socket is probably broken. Abort the socket and propagate the exception
                    receiving.GetAwaiter().GetResult();

                    // Should never get here because GetResult above will throw
                    Debug.Fail("GetResult didn't throw?");
                    return;
                }

                // Shutting down because we received a close frame from the client.
                // Complete the input writer so that the application knows there won't be any more input.
                _logger.LogDebug("Client closed connection with status code '{0}' ({1}). Signaling end-of-input to application", receiving.Result.Status, receiving.Result.Description);
                _application.Output.TryComplete();

                // Wait for the application to finish sending.
                _logger.LogDebug("Waiting for the application to finish sending data");
                await sending;

                // Send the server's close frame
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure);
            }
            else
            {
                var failed = sending.IsFaulted || _application.Input.Completion.IsFaulted;

                // The application finished sending. Close our end of the connection
                _logger.LogDebug(!failed ? "Application finished sending. Sending close frame." : "Application failed during sending. Sending InternalServerError close frame");
                await socket.CloseAsync(!failed ? WebSocketCloseStatus.NormalClosure : WebSocketCloseStatus.InternalServerError);

                // Now trigger the exception from the application, if there was one.
                sending.GetAwaiter().GetResult();

                _logger.LogDebug("Waiting for the client to close the socket");

                // Wait for the client to close or wait for the close timeout
                var resultTask = await Task.WhenAny(receiving, Task.Delay(_options.CloseTimeout));

                // We timed out waiting for the transport to close so abort the connection so we don't attempt to write anything else
                if (resultTask != receiving)
                {
                    _logger.LogDebug("Timed out waiting for client to send the close frame, aborting the connection.");
                    socket.Abort();
                }

                // We're done writing
                _application.Output.TryComplete();
            }
        }

        private Task HandleFrame(WebSocketFrame frame)
        {
            // Is this a frame we care about?
            if (!frame.Opcode.IsMessage())
            {
                return Task.CompletedTask;
            }

            LogFrame("Receiving", frame);

            // Determine the effective opcode based on the continuation.
            var effectiveOpcode = frame.Opcode;
            if (frame.Opcode == WebSocketOpcode.Continuation)
            {
                effectiveOpcode = _lastOpcode;
            }
            else
            {
                _lastOpcode = frame.Opcode;
            }

            // Create a Message for the frame
            // This has to copy the buffer :(.
            var message = new Message(frame.Payload.ToArray(), effectiveOpcode == WebSocketOpcode.Binary ? MessageType.Binary : MessageType.Text, frame.EndOfMessage);

            // Write the message to the channel
            return _application.Output.WriteAsync(message);
        }

        private void LogFrame(string action, WebSocketFrame frame)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(
                    $"{action} frame: Opcode={frame.Opcode}, Fin={frame.EndOfMessage}, Payload={frame.Payload.Length} bytes");
            }
        }

        private async Task StartSending(IWebSocketConnection ws)
        {
            while (await _application.Input.WaitToReadAsync())
            {
                // Get a frame from the application
                Message message;
                while (_application.Input.TryRead(out message))
                {
                    if (message.Payload.Length > 0)
                    {
                        try
                        {
                            var opcode = message.Type == MessageType.Binary ?
                                WebSocketOpcode.Binary :
                                WebSocketOpcode.Text;

                            var frame = new WebSocketFrame(
                                endOfMessage: message.EndOfMessage,
                                opcode: _lastFrameIncomplete ? WebSocketOpcode.Continuation : opcode,
                                payload: ReadableBuffer.Create(message.Payload));

                            _lastFrameIncomplete = !message.EndOfMessage;

                            LogFrame("Sending", frame);
                            await ws.SendAsync(frame);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError("Error writing frame to output: {0}", ex);
                            break;
                        }
                    }
                }
            }
        }
    }
}
