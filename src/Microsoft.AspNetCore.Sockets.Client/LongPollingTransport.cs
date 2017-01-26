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
        private IChannelConnection<Message> _application;
        private Task _sender;
        private Task _poller;
        private readonly CancellationTokenSource _transportCts = new CancellationTokenSource();

        public Task Running { get; private set; }

        public LongPollingTransport(HttpClient httpClient, ILoggerFactory loggerFactory)
        {
            _httpClient = httpClient;
            _logger = loggerFactory.CreateLogger<LongPollingTransport>();
        }

        public void Dispose()
        {
            _transportCts.Cancel();
        }

        public Task StartAsync(Uri url, IChannelConnection<Message> application)
        {
            _application = application;

            // Start sending and polling
            _poller = Poll(Utils.AppendPath(url, "poll"), _transportCts.Token).ContinueWith(_ => _transportCts.Cancel());
            _sender = SendMessages(Utils.AppendPath(url, "send"), _transportCts.Token).ContinueWith(_ => _transportCts.Cancel());
            Running = Task.WhenAll(_sender, _poller);

            return TaskCache.CompletedTask;
        }

        private async Task Poll(Uri pollUrl, CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, pollUrl);
                    request.Headers.UserAgent.Add(DefaultUserAgentHeader);

                    var response = await _httpClient.SendAsync(request, cancellationToken);
                    response.EnsureSuccessStatusCode();

                    if (response.StatusCode == HttpStatusCode.NoContent || cancellationToken.IsCancellationRequested)
                    {
                        // Transport closed or polling stopped, we're done
                        break;
                    }
                    else
                    {
                        var ms = new MemoryStream();
                        await response.Content.CopyToAsync(ms);
                        var message = new Message(ReadableBuffer.Create(ms.ToArray()).Preserve(), Format.Text);

                        while (await _application.Output.WaitToWriteAsync(cancellationToken))
                        {
                            if (_application.Output.TryWrite(message))
                            {
                                break;
                            }
                        }
                    }
                }

                _application.Output.TryComplete();
            }
            catch (OperationCanceledException)
            {
                // transport is being closed
                _application.Output.TryComplete();
            }
            catch (Exception ex)
            {
                _logger.LogError("Error while polling '{0}': {1}", pollUrl, ex);
                _application.Output.TryComplete(ex);
            }

            _transportCts.Cancel();
        }

        private async Task SendMessages(Uri sendUrl, CancellationToken cancellationToken)
        {
            try
            {
                while (await _application.Input.WaitToReadAsync(cancellationToken))
                {
                    Message message;
                    while (!cancellationToken.IsCancellationRequested && _application.Input.TryRead(out message))
                    {
                        using (message)
                        {
                            var request = new HttpRequestMessage(HttpMethod.Post, sendUrl);
                            request.Headers.UserAgent.Add(DefaultUserAgentHeader);
                            request.Content = new ReadableBufferContent(message.Payload.Buffer);

                            var response = await _httpClient.SendAsync(request);
                            response.EnsureSuccessStatusCode();
                        }
                    }
                }

                _application.Output.TryComplete();
            }
            catch (OperationCanceledException)
            {
                // transport is being closed
                _application.Output.TryComplete();
            }
            catch (Exception ex)
            {
                _logger.LogError("Error while sending to '{0}': {1}", sendUrl, ex);
                _application.Output.TryComplete(ex);
            }
        }
    }
}
