// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.IO.Pipelines.Text.Primitives;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Formatting;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Sockets.Client.Internal;
using Microsoft.AspNetCore.Sockets.Internal.Formatters;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.AspNetCore.Sockets.Client
{
    public class HttpStreamingTransport : ITransport, IHttpHeadersHandler, IHttpResponseLineHandler
    {
        private static readonly string DefaultUserAgent = "Microsoft.AspNetCore.SignalR.Client/0.0.0";
        private static readonly ProductInfoHeaderValue DefaultUserAgentHeader = ProductInfoHeaderValue.Parse(DefaultUserAgent);

        private readonly HttpClient _httpClient;
        private readonly ILogger _logger;
        private IChannelConnection<SendMessage, Message> _application;

        private readonly CancellationTokenSource _transportCts = new CancellationTokenSource();
        private readonly HttpParser _parser = new HttpParser();
        private bool _parsingHeaders;
        private string _boundary;

        public Task Running { get; private set; } = Task.CompletedTask;

        public HttpStreamingTransport(HttpClient httpClient)
            : this(httpClient, null)
        { }

        public HttpStreamingTransport(HttpClient httpClient, ILoggerFactory loggerFactory)
        {
            _httpClient = httpClient;
            _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<HttpStreamingTransport>();
        }

        public Task StartAsync(Uri url, IChannelConnection<SendMessage, Message> application)
        {
            _logger.LogInformation("Starting {0}", nameof(HttpStreamingTransport));

            _application = application;

            var streamingUrl = Utils.AppendPath(url, "streaming");

            Running = Start(streamingUrl, _transportCts.Token).ContinueWith(t =>
            {
                _logger.LogDebug("Transport stopped. Exception: '{0}'", t.Exception?.InnerException);

                _application.Output.TryComplete(t.IsFaulted ? t.Exception.InnerException : null);
                return t;
            }).Unwrap();

            return TaskCache.CompletedTask;
        }

        public async Task StopAsync()
        {
            _logger.LogInformation("Transport {0} is stopping", nameof(LongPollingTransport));

            _transportCts.Cancel();

            try
            {
                await Running;
            }
            catch
            {
                // exceptions have been handled in the Running task continuation by closing the channel with the exception
            }

            _logger.LogInformation("Transport {0} stopped", nameof(LongPollingTransport));
        }

        private async Task Start(Uri streamingUrl, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting the receive loop");
            try
            {
                var client = new TcpClient(AddressFamily.InterNetwork);
                await client.ConnectAsync(streamingUrl.Host, streamingUrl.Port);
                var boundary = Guid.NewGuid().ToString();

                // Get the network stream and send a http request
                var stream = client.GetStream();
                var writer = stream.AsPipelineWriter();
                var reader = stream.AsPipelineReader();

                var output = writer.Alloc();
                output.Append($"POST {streamingUrl.GetComponents(UriComponents.PathAndQuery, UriFormat.SafeUnescaped)} HTTP/1.1\r\n", TextEncoder.Utf8);
                output.Append("Transfer-Encoding: chunked\r\n", TextEncoder.Utf8);
                output.Append($"Content-Type: multipart/related; boundary={boundary}\r\n", TextEncoder.Utf8);
                // End of request
                output.Append("\r\n", TextEncoder.Utf8);
                await output.FlushAsync();

                // Read the response
                while (true)
                {
                    var result = await reader.ReadAsync();
                    var buffer = result.Buffer;
                    var consumed = buffer.Start;
                    var examined = buffer.End;

                    try
                    {
                        if (result.IsCompleted && buffer.IsEmpty)
                        {
                            throw new InvalidOperationException("Bad response");
                        }

                        if (TryParseResponse(buffer, out consumed, out examined))
                        {
                            break;
                        }
                    }
                    finally
                    {
                        reader.Advance(consumed, examined);
                    }
                }

                var writing = ReadFromApplicationSendToTransport(writer, boundary, cancellationToken);
                var reading = ReadFromTransportWriteToApplication(stream, cancellationToken);

                await Task.WhenAll(reading, writing);
            }
            catch (OperationCanceledException)
            {
                // transport is being closed
            }
            catch (Exception ex)
            {
                _logger.LogError("Error while streaming '{0}': {1}", streamingUrl, ex);
                throw;
            }
            finally
            {
                // Make sure the send loop is terminated
                _transportCts.Cancel();
                _logger.LogInformation("Receive loop stopped");
            }
        }

        private bool TryParseResponse(ReadableBuffer buffer, out ReadCursor consumed, out ReadCursor examined)
        {
            consumed = buffer.Start;
            examined = buffer.End;

            if (!_parsingHeaders)
            {
                if (_parser.ParseResponseLine(this, buffer, out consumed, out examined))
                {
                    buffer = buffer.Slice(consumed);

                    _parsingHeaders = true;
                }
                else
                {
                    return false;
                }
            }

            if (_parsingHeaders)
            {
                if (_parser.ParseHeaders(this, buffer, out consumed, out examined, out var consumedBytes))
                {
                    return true;
                }
            }

            return false;
        }

        private async Task ReadFromTransportWriteToApplication(Stream stream, CancellationToken cancellationToken)
        {
            var reader = new MultipartReader(_boundary, stream);
            while (true)
            {
                var section = await reader.ReadNextSectionAsync();
                if (section == null)
                {
                    break;
                }

                if (!Microsoft.Net.Http.Headers.MediaTypeHeaderValue.TryParse(section.ContentType, out var sectionContentTypeHeader))
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
                using (var bodyStream = new MemoryStream())
                {
                    await section.Body.CopyToAsync(bodyStream);
                    buffer = bodyStream.ToArray();
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

        private async Task ReadFromApplicationSendToTransport(IPipeWriter writer, string boundary, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting the send loop");
            IList<SendMessage> messages = null;
            try
            {
                while (await _application.Input.WaitToReadAsync(cancellationToken))
                {
                    // Grab as many messages as we can from the channel
                    messages = new List<SendMessage>();
                    while (!cancellationToken.IsCancellationRequested && _application.Input.TryRead(out SendMessage message))
                    {
                        messages.Add(message);
                    }

                    int length = 0;
                    if (messages.Count > 0)
                    {
                        foreach (var message in messages)
                        {
                            // Boundary
                            length += 2 + boundary.Length + 2;
                            length += "Content-Type: ".Length;
                            length += message.Type == MessageType.Text ? MessageFormatter.TextContentType.Length : MessageFormatter.BinaryContentType.Length;
                            length += 4;
                            length += message.Payload.Length;
                            length += 4;
                            length += 2 + boundary.Length + 6;
                        }
                    }

                    var preamble = 0;

                    if (messages.Count > 0)
                    {
                        var buffer = writer.Alloc();
                        buffer.Append(length, TextEncoder.Utf8, TextFormat.HexLowercase);
                        buffer.Append("\r\n", TextEncoder.Utf8);
                        preamble = buffer.BytesWritten;
                        foreach (var message in messages)
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

                            message.SendResult.TrySetResult(null);
                        }

                        buffer.Append("--" + boundary + "\r\n\r\n\r\n", TextEncoder.Utf8);
                        buffer.Append("\r\n", TextEncoder.Utf8);
                        preamble += 2;

                        var written = buffer.AsReadableBuffer();
                        _logger.LogDebug("Writing {bytes}: {content}", written.Length, written);
                        _logger.LogDebug("Calculated {bytes} bytes = {b2}", length, written.Length - preamble);

                        await buffer.FlushAsync();
                    }

                    _logger.LogDebug("Sending {0} message(s) to the server", messages.Count);
                }
            }
            catch (OperationCanceledException)
            {
                // transport is being closed
                if (messages != null)
                {
                    foreach (var message in messages)
                    {
                        // This will no-op for any messages that were already marked as completed.
                        message.SendResult?.TrySetCanceled();
                    }
                }
            }
            catch (Exception ex)
            {
                // _logger.LogError("Error while sending to '{0}': {1}", sendUrl, ex);
                if (messages != null)
                {
                    foreach (var message in messages)
                    {
                        // This will no-op for any messages that were already marked as completed.
                        message.SendResult?.TrySetException(ex);
                    }
                }
                throw;
            }
            finally
            {
                // Make sure the poll loop is terminated
                _transportCts.Cancel();

                writer.Complete();
            }

            _logger.LogInformation("Send loop stopped");
        }

        public void OnHeader(Span<byte> name, Span<byte> value)
        {
            if (string.Equals("Content-Type", name.GetAsciiStringNonNullCharacters(), StringComparison.OrdinalIgnoreCase))
            {
                if (Microsoft.Net.Http.Headers.MediaTypeHeaderValue.TryParse(value.GetAsciiStringNonNullCharacters(), out var contentTypeHeader))
                {
                    _boundary = contentTypeHeader.Boundary;
                }
            }
        }

        public void OnStartLine(HttpVersion version, int status, Span<byte> statusText)
        {
            if (status > 299)
            {
                // Blow up
            }
        }
    }
}
