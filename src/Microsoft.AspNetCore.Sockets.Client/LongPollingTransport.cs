// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.IO.Pipelines;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Sockets.Client
{
    public class LongPollingTransport : ITransport
    {
        private static readonly string DefaultUserAgent = "Microsoft.AspNetCore.SignalR.Client/0.0.0";
        private static readonly ProductInfoHeaderValue DefaultUserAgentHeader = ProductInfoHeaderValue.Parse(DefaultUserAgent);

        private readonly HttpClient _httpClient;
        private readonly ILogger _logger;
        private readonly CancellationTokenSource _senderCts = new CancellationTokenSource();
        private readonly CancellationTokenSource _pollCts = new CancellationTokenSource();

        private IChannelConnection<Message> _toFromConnection;

        private Task _sender;
        private Task _poller;

        public Task Running { get; private set; }

        public LongPollingTransport(HttpClient httpClient, ILoggerFactory loggerFactory)
        {
            _httpClient = httpClient;
            _logger = loggerFactory.CreateLogger<LongPollingTransport>();
        }

        public void Dispose()
        {
            _senderCts.Cancel();
            _pollCts.Cancel();
        }

        public Task StartAsync(Uri url, IChannelConnection<Message> toFromConnection)
        {
            _toFromConnection = toFromConnection;

            // Start sending and polling
            _poller = Poll(Utils.AppendPath(url, "poll"), _pollCts.Token).ContinueWith(_ => _senderCts.Cancel());
            _sender = SendMessages(Utils.AppendPath(url, "send"), _senderCts.Token).ContinueWith(_ => _pollCts.Cancel());
            Running = Task.WhenAll(_sender, _poller);

            return TaskCache.CompletedTask;
        }

        private async Task Poll(Uri pollUrl, CancellationToken pollCancellationToken)
        {
            try
            {
                while (!pollCancellationToken.IsCancellationRequested)
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, pollUrl);
                    request.Headers.UserAgent.Add(DefaultUserAgentHeader);

                    var response = await _httpClient.SendAsync(request, pollCancellationToken);
                    response.EnsureSuccessStatusCode();

                    if (response.StatusCode == HttpStatusCode.NoContent || pollCancellationToken.IsCancellationRequested)
                    {
                        // Transport closed or polling stopped, we're done
                        break;
                    }
                    else
                    {
                        var ms = new MemoryStream();
                        await response.Content.CopyToAsync(ms);
                        var message = new Message(ReadableBuffer.Create(ms.ToArray()).Preserve(), Format.Text);

                        while (await _toFromConnection.Output.WaitToWriteAsync(pollCancellationToken))
                        {
                            if (_toFromConnection.Output.TryWrite(message))
                            {
                                break;
                            }
                        }
                    }
                }

                _toFromConnection.Output.TryComplete();
            }
            catch (OperationCanceledException)
            {
                // transport is being closed
                _toFromConnection.Output.TryComplete();
            }
            catch (Exception ex)
            {
                _logger.LogError("Error while polling '{0}': {1}", pollUrl, ex);
                _toFromConnection.Output.TryComplete(ex);
            }
        }

        private async Task SendMessages(Uri sendUrl, CancellationToken sendCancellationToken)
        {
            try
            {
                while (await _toFromConnection.Input.WaitToReadAsync(sendCancellationToken))
                {
                    Message message;
                    if (!_toFromConnection.Input.TryRead(out message))
                    {
                        continue;
                    }

                    var request = new HttpRequestMessage(HttpMethod.Post, sendUrl);
                    request.Headers.UserAgent.Add(DefaultUserAgentHeader);
                    request.Content = new ReadableBufferContent(message.Payload.Buffer);

                    var response = await _httpClient.SendAsync(request);
                    response.EnsureSuccessStatusCode();
                }

                _toFromConnection.Output.TryComplete();
            }
            catch (OperationCanceledException)
            {
                // transport is being closed
                _toFromConnection.Output.TryComplete();
            }
            catch (Exception ex)
            {
                _logger.LogError("Error while sending to '{0}': {1}", sendUrl, ex);
                _toFromConnection.Output.TryComplete(ex);
            }
        }
    }
}
