// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.IO;
using System.IO.Pipelines;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Sockets.Client.Http;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Sockets.Client
{
    internal static class SendUtils
    {
        public static async Task SendMessages(Uri sendUrl, IDuplexPipe application, HttpClient httpClient,
            HttpOptions httpOptions, CancellationTokenSource transportCts, ILogger logger)
        {
            Log.SendStarted(logger);

            try
            {
                while (true)
                {
                    var result = await application.Input.ReadAsync(transportCts.Token);
                    var buffer = result.Buffer;

                    try
                    {
                        // Grab as many messages as we can from the pipe

                        transportCts.Token.ThrowIfCancellationRequested();
                        if (!buffer.IsEmpty)
                        {
                            Log.SendingMessages(logger, buffer.Length, sendUrl);

                            // Send them in a single post
                            var request = new HttpRequestMessage(HttpMethod.Post, sendUrl);
                            PrepareHttpRequest(request, httpOptions);

                            request.Content = new ReadOnlyBufferContent(buffer);

                            var response = await httpClient.SendAsync(request, transportCts.Token);
                            response.EnsureSuccessStatusCode();

                            Log.SentSuccessfully(logger);
                        }
                        else if (result.IsCompleted)
                        {
                            break;
                        }
                        else
                        {
                            Log.NoMessages(logger);
                        }
                    }
                    finally
                    {
                        application.Input.AdvanceTo(buffer.End);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Log.SendCanceled(logger);
            }
            catch (Exception ex)
            {
                Log.ErrorSending(logger, sendUrl, ex);
                throw;
            }
            finally
            {
                // Make sure the poll loop is terminated
                transportCts.Cancel();
            }

            Log.SendStopped(logger);
        }

        public static void PrepareHttpRequest(HttpRequestMessage request, HttpOptions httpOptions)
        {
            if (httpOptions?.Headers != null)
            {
                foreach (var header in httpOptions.Headers)
                {
                    request.Headers.Add(header.Key, header.Value);
                }
            }
            request.Headers.UserAgent.Add(Constants.UserAgentHeader);

            if (httpOptions?.AccessTokenFactory != null)
            {
                request.Headers.Add("Authorization", $"Bearer {httpOptions.AccessTokenFactory()}");
            }
        }

        private class ReadOnlyBufferContent : HttpContent
        {
            private readonly ReadOnlyBuffer<byte> _buffer;

            public ReadOnlyBufferContent(ReadOnlyBuffer<byte> buffer)
            {
                _buffer = buffer;
            }

            protected override Task SerializeToStreamAsync(Stream stream, TransportContext context)
            {
                return stream.WriteAsync(_buffer);
            }

            protected override bool TryComputeLength(out long length)
            {
                length = _buffer.Length;
                return true;
            }
        }

        private static class Log
        {
            private static readonly Action<ILogger, Exception> _sendStarted =
                LoggerMessage.Define(LogLevel.Debug, new EventId(100, "SendStarted"), "Starting the send loop.");

            private static readonly Action<ILogger, Exception> _sendStopped =
                LoggerMessage.Define(LogLevel.Debug, new EventId(101, "SendStopped"), "Send loop stopped.");

            private static readonly Action<ILogger, Exception> _sendCanceled =
                LoggerMessage.Define(LogLevel.Debug, new EventId(102, "SendCanceled"), "Send loop canceled.");

            private static readonly Action<ILogger, long, Uri, Exception> _sendingMessages =
                LoggerMessage.Define<long, Uri>(LogLevel.Debug, new EventId(103, "SendingMessages"), "Sending {count} bytes to the server using url: {url}.");

            private static readonly Action<ILogger, Exception> _sentSuccessfully =
                LoggerMessage.Define(LogLevel.Debug, new EventId(104, "SentSuccessfully"), "Message(s) sent successfully.");

            private static readonly Action<ILogger, Exception> _noMessages =
                LoggerMessage.Define(LogLevel.Debug, new EventId(105, "NoMessages"), "No messages in batch to send.");

            private static readonly Action<ILogger, Uri, Exception> _errorSending =
                LoggerMessage.Define<Uri>(LogLevel.Error, new EventId(106, "ErrorSending"), "Error while sending to '{url}'.");

            // When adding a new log message make sure to check with LongPollingTransport and ServerSentEventsTransport that share these logs to not have conflicting EventIds
            // We start the IDs at 100 to make it easy to avoid conflicting IDs

            public static void SendStarted(ILogger logger)
            {
                _sendStarted(logger, null);
            }

            public static void SendCanceled(ILogger logger)
            {
                _sendCanceled(logger, null);
            }

            public static void SendStopped(ILogger logger)
            {
                _sendStopped(logger, null);
            }

            public static void SendingMessages(ILogger logger, long count, Uri url)
            {
                _sendingMessages(logger, count, url, null);
            }

            public static void SentSuccessfully(ILogger logger)
            {
                _sentSuccessfully(logger, null);
            }

            public static void NoMessages(ILogger logger)
            {
                _noMessages(logger, null);
            }

            public static void ErrorSending(ILogger logger, Uri url, Exception exception)
            {
                _errorSending(logger, url, exception);
            }
        }
    }
}
