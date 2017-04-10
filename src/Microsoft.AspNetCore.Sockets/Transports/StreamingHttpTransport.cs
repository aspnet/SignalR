using System;
using System.IO;
using System.IO.Pipelines;
using System.Text;
using System.Text.Formatting;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Sockets.Internal.Formatters;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace Microsoft.AspNetCore.Sockets.Transports
{
    public class StreamingHttpTransport : IHttpTransport
    {
        private readonly ILogger _logger;
        private readonly IChannelConnection<Message> _application;

        public StreamingHttpTransport(IChannelConnection<Message> application, ILoggerFactory loggerFactory)
        {
            if (application == null)
            {
                throw new ArgumentNullException(nameof(application));
            }

            if (loggerFactory == null)
            {
                throw new ArgumentNullException(nameof(loggerFactory));
            }

            _application = application;
            _logger = loggerFactory.CreateLogger<StreamingHttpTransport>();
        }

        public Task ProcessRequestAsync(HttpContext context, CancellationToken token)
        {
            var reading = Reading(context, token);
            var writing = Writing(context, token);
            return Task.WhenAll(reading, writing);
        }

        private async Task Reading(HttpContext context, CancellationToken token)
        {
            _logger.LogDebug("Received request to streaming transport");

            if (!MediaTypeHeaderValue.TryParse(context.Request.ContentType, out var contentTypeHeader))
            {
                _logger.LogError("Invalid content type header");
                return;
            }

            if (string.IsNullOrEmpty(contentTypeHeader.Boundary))
            {
                _logger.LogDebug("No content type found");
                return;
            }

            _logger.LogDebug("Reading multipart boundary {boundary}", contentTypeHeader.Boundary);
            var reader = new MultipartReader(contentTypeHeader.Boundary, context.Request.Body);

            while (true)
            {
                var section = await reader.ReadNextSectionAsync();
                if (section == null)
                {
                    break;
                }

                if (!MediaTypeHeaderValue.TryParse(section.ContentType, out var sectionContentTypeHeader))
                {
                    // Likely an empty frame
                    continue;
                }

                var messageType = MessageType.Text;

                if (string.Equals(sectionContentTypeHeader.MediaType, MessageFormatter.BinaryContentType))
                {
                    messageType = MessageType.Binary;
                }
                else if (string.Equals(sectionContentTypeHeader.MediaType, MessageFormatter.TextContentType))
                {
                    messageType = MessageType.Text;
                }
                else
                {
                    // Unknown message type
                    continue;
                }

                byte[] buffer;
                using (var stream = new MemoryStream())
                {
                    await section.Body.CopyToAsync(stream);
                    buffer = stream.ToArray();
                }

                var message = new Message(buffer, messageType);

                _logger.LogDebug("Received message {type} of length {length}", message.Type, message.Payload.Length);

                while (!_application.Output.TryWrite(message))
                {
                    if (!await _application.Output.WaitToWriteAsync())
                    {
                        break;
                    }
                }
            }
        }

        private async Task Writing(HttpContext context, CancellationToken token)
        {
            var boundary = Guid.NewGuid();
            context.Response.ContentType = $"multipart/related; boundary={boundary};";
            context.Response.Headers["Cache-Control"] = "no-cache";
            context.Response.Headers["Content-Encoding"] = "identity";

            await context.Response.Body.FlushAsync();

            var writer = context.Response.Body.AsPipelineWriter();

            while (await _application.Input.WaitToReadAsync(token))
            {
                var buffer = writer.Alloc();
                while (_application.Input.TryRead(out var message))
                {
                    // Write to output
                    // Write boundary
                    buffer.Append("--" + boundary + "\r\n", TextEncoder.Utf8);

                    // Write headers 
                    buffer.Append("Content-Type: ", TextEncoder.Utf8);
                    if (message.Type == MessageType.Text)
                    {
                        buffer.Append(MessageFormatter.TextContentType, TextEncoder.Utf8);
                    }
                    else
                    {
                        buffer.Append(MessageFormatter.BinaryContentType, TextEncoder.Utf8);
                    }
                    buffer.Append("\r\n\r\n", TextEncoder.Utf8);
                    // Write content
                    buffer.Write(message.Payload);
                    buffer.Append("\r\n\r\n", TextEncoder.Utf8);
                }

                // Send an empty frame so that the client can find the end of this batch
                buffer.Append("--" + boundary + "\r\n\r\n", TextEncoder.Utf8);

                _logger.LogDebug("Writing {content}", buffer.AsReadableBuffer());
                await buffer.FlushAsync();
            }
        }
    }
}
