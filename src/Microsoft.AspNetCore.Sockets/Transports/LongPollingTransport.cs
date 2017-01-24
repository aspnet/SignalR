// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO.Pipelines;
using System.Threading.Tasks;
using System.Threading.Tasks.Channels;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Sockets.Transports
{
    public class LongPollingTransport : IHttpTransport
    {
        public static readonly string TransportName = "longPolling";

        private readonly ReadableChannel<Message> _application;
        private readonly ILogger _logger;

        public string Name { get; } = TransportName;

        public LongPollingTransport(ReadableChannel<Message> application, ILoggerFactory loggerFactory)
        {
            _application = application;
            _logger = loggerFactory.CreateLogger<LongPollingTransport>();
        }

        public async Task ProcessRequestAsync(HttpContext context)
        {
            try
            {
                // TODO: We need the ability to yield the connection without completing the channel.
                // This is to force ReadAsync to yield without data to end to poll but not the entire connection.
                // This is for cases when the client reconnects see issue #27
                if (!await _application.WaitToReadAsync(context.RequestAborted))
                {
                    if(_application.Completion.IsFaulted)
                    {
                        _logger.LogError("Application terminated connection with error: {0}", _application.Completion.Exception.InnerException);
                    }

                    _logger.LogInformation("Terminating Long Polling connection by sending 204 response.");
                    context.Response.StatusCode = 204;
                    return;
                }

                Message message;
                if (_application.TryRead(out message))
                {
                    using (message)
                    {
                        _logger.LogDebug("Writing {0} byte message to response", message.Payload.Buffer.Length);
                        context.Response.ContentLength = message.Payload.Buffer.Length;
                        await message.Payload.Buffer.CopyToAsync(context.Response.Body);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Suppress the exception
                _logger.LogDebug("Client disconnected from Long Polling endpoint.");
            }
            catch (Exception ex)
            {
                _logger.LogError("Error reading next message from Application: {0}", ex);
            }
        }
    }
}
