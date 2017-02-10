// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO.Pipelines;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Sockets.Client
{
    public class WebSocketsTransport : ITransport
    {
        private ClientWebSocket _webSocket = new ClientWebSocket();
        private IChannelConnection<Message> _application;
        private CancellationToken _cancellationToken = new CancellationToken();
        private readonly ILogger _logger;

        public WebSocketsTransport()
        {
            _logger = NullLoggerFactory.Instance.CreateLogger("WebSocketsTransport");
        }

        public WebSocketsTransport(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<WebSocketsTransport>();
        }
        public Task Running { get; private set; }
                                                
        public async Task StartAsync(Uri url, IChannelConnection<Message> application)  
        {
            if (url == null)
            {
                throw new ArgumentNullException(nameof(url));
            }

            if (application == null)
            {
                throw new ArgumentNullException(nameof(application));
            }

            _application = application;
            await Connect(url);
            var sendTask = SendMessages(url, _cancellationToken);
            var receiveTask = ReceiveMessages(url, _cancellationToken);

            Running = Task.WhenAll(sendTask, receiveTask).ContinueWith(t => {
                _application.Output.TryComplete(t.IsFaulted ? t.Exception.InnerException : null);
                return t;
            }).Unwrap();
        }

        private async Task ReceiveMessages(Uri pollUrl, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                const int bufferSize = 1024;
                var totalBytes = 0;
                var incomingMessage = new List<ArraySegment<byte>>();
                WebSocketReceiveResult receiveResult;
                do
                {
                    var buffer = new ArraySegment<byte>(new byte[bufferSize]);
                    receiveResult = await _webSocket.ReceiveAsync(buffer, cancellationToken);
                    incomingMessage.Add(buffer);
                    totalBytes += receiveResult.Count;
                } while (!receiveResult.EndOfMessage);

                Message message;
                if (incomingMessage.Count > 1)
                {
                    var messageBuffer = new byte[totalBytes];
                    var offset = 0;
                    for (var i = 0 ; i < incomingMessage.Count; i++)
                    {
                        Buffer.BlockCopy(incomingMessage[i].Array, 0, messageBuffer, offset, incomingMessage[i].Count);
                        offset += incomingMessage[i].Count;
                    }
                    
                    message = new Message(ReadableBuffer.Create(messageBuffer).Preserve(), receiveResult.MessageType == 0 ? Format.Text : Format.Binary, receiveResult.EndOfMessage);
                }
                else
                {
                    message = new Message(ReadableBuffer.Create(incomingMessage[0].Array).Preserve(), receiveResult.MessageType == 0 ? Format.Text : Format.Binary, receiveResult.EndOfMessage);
                }

                while (await _application.Output.WaitToWriteAsync(cancellationToken))
                {
                    if (_application.Output.TryWrite(message))
                    {
                        incomingMessage.Clear();
                        break;
                    }
                }
            }
        }

        private async Task SendMessages(Uri sendUrl, CancellationToken cancellationToken)
        {
            while (await _application.Input.WaitToReadAsync(cancellationToken))
            {
                Message message;
                while (_application.Input.TryRead(out message))
                {
                    using (message)
                    {
                        try
                        {
                            await _webSocket.SendAsync(new ArraySegment<byte>(message.Payload.Buffer.ToArray()),
                            message.MessageFormat == Format.Text ? WebSocketMessageType.Text : WebSocketMessageType.Binary, true,
                            cancellationToken);
                        } catch(OperationCanceledException ex)
                        {
                            _logger?.LogError(ex.Message);
                            await _webSocket.CloseAsync(WebSocketCloseStatus.Empty, null, _cancellationToken);
                            break;
                        }
                    }
                }
            }
        }

        private async Task Connect(Uri url)
        {
            var uriBuilder = new UriBuilder(url);
            if (url.Scheme == "http")
            {
                uriBuilder.Scheme = "ws";
            }
            else if (url.Scheme == "https")
            {
                uriBuilder.Scheme = "wss";
            }

            await _webSocket.ConnectAsync(uriBuilder.Uri, _cancellationToken);
        }

        public void Dispose()
        {
            _webSocket.Dispose();
        }

        public async Task StopAsync()
        {
            await _webSocket.CloseAsync(WebSocketCloseStatus.Empty, null, _cancellationToken);
        }
    }
}
