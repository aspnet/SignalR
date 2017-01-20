// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using System.Threading.Tasks.Channels;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Sockets.Transports
{
    public class ServerSentEventsTransport : IHttpTransport
    {
        public static readonly string TransportName = "serverSentEvents";
        private readonly ReadableChannel<Message> _application;
        private readonly ILogger _logger;

        public string Name { get; } = TransportName;

        public ServerSentEventsTransport(ReadableChannel<Message> application, ILoggerFactory loggerFactory)
        {
            _application = application;
            _logger = loggerFactory.CreateLogger<ServerSentEventsTransport>();
        }

        public async Task ProcessRequestAsync(HttpContext context)
        {
            context.Response.ContentType = "text/event-stream";

            // Working around dynamic compression behavior in ANCM: https://github.com/aspnet/AspNetCoreModule/issues/16
            context.Response.Headers["Cache-Control"] = "no-cache";
            context.Response.Headers["Content-Encoding"] = "identity";

            await context.Response.Body.FlushAsync();

            try
            {
                while (await _application.WaitToReadAsync(context.RequestAborted))
                {
                    Message message;
                    while (_application.TryRead(out message))
                    {
                        using (message)
                        {
                            await Send(context, message);
                        }
                    }
                }

                if (_application.Completion.IsFaulted)
                {
                    _logger.LogError("Application terminated connection with error: {0}", _application.Completion.Exception.InnerException);
                }
            }
            catch (OperationCanceledException)
            {
                // Suppress the exception
                _logger.LogDebug("Client disconnected from Server-Sent Events endpoint.");
            }
            catch (Exception ex)
            {
                _logger.LogError("Error reading next message from Application: {0}", ex);
            }
        }

        private async Task Send(HttpContext context, Message message)
        {
            // TODO: Pooled buffers
            // 8 = 6(data: ) + 2 (\n\n)
            _logger.LogDebug("Sending {0} byte message to Server-Sent Events client", message.Payload.Buffer.Length);
            var buffer = new byte[8 + message.Payload.Buffer.Length];
            var at = 0;
            buffer[at++] = (byte)'d';
            buffer[at++] = (byte)'a';
            buffer[at++] = (byte)'t';
            buffer[at++] = (byte)'a';
            buffer[at++] = (byte)':';
            buffer[at++] = (byte)' ';
            message.Payload.Buffer.CopyTo(new Span<byte>(buffer, at, message.Payload.Buffer.Length));
            at += message.Payload.Buffer.Length;
            buffer[at++] = (byte)'\n';
            buffer[at++] = (byte)'\n';
            await context.Response.Body.WriteAsync(buffer, 0, at);
            await context.Response.Body.FlushAsync();
        }
    }
}
