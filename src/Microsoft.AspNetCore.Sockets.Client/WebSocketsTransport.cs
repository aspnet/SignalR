// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO.Pipelines;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;

namespace Microsoft.AspNetCore.Sockets.Client
{
    public class WebSocketsTransport : ITransport
    {
        private ClientWebSocket _webSocket = new ClientWebSocket();
        private IChannelConnection<Message> _application;
        private CancellationToken token = new CancellationToken();

        public Task Running { get; private set; }

        public WebSocketsTransport()
        {
        }
                                                
        public async Task StartAsync(Uri url, IChannelConnection<Message> application)  
        {
            _application = application;
            var webSocketUrl = url.ToString();
            await Connect(webSocketUrl);
            var sendTask = SendMessages(url, token);
            var receiveTask = ReceiveMessages(url, token);

            Running = Task.WhenAll(sendTask, receiveTask).ContinueWith(t => {
                _application.Output.TryComplete(t.IsFaulted ? t.Exception.InnerException : null);
                return t;
            }).Unwrap();
        }

        public async Task ReceiveMessages(Uri pollUrl, CancellationToken cancellationToken)
        {
            var incomingMessage = new List<byte[]>();
            while (!cancellationToken.IsCancellationRequested)
            {
                var buffer = new ArraySegment<byte>(new byte[1024]);
                var receiveResult = await _webSocket.ReceiveAsync(buffer, cancellationToken);
                var totalBytes = 0;
                var completeMessage = receiveResult.EndOfMessage;
                while (!completeMessage)
                {
                    completeMessage = receiveResult.EndOfMessage;
                    incomingMessage.Add(buffer.Array);
                    totalBytes += buffer.Count;
                    buffer = new ArraySegment<byte>(new byte[2]);
                    receiveResult = await _webSocket.ReceiveAsync(buffer, cancellationToken);
                }

                Message message;
                if (incomingMessage.Count > 1)
                {
                    var totalBuffer = new byte[totalBytes];
                    var offset = 0;
                    for (int i = 0 ; i < incomingMessage.Count; i++)
                    {
                        System.Buffer.BlockCopy(incomingMessage[i], 0, totalBuffer, offset, incomingMessage[i].Length);
                        offset += incomingMessage[i].Length;
                    }

                    message = new Message(ReadableBuffer.Create(totalBuffer).Preserve(), Format.Text, receiveResult.EndOfMessage);
                }
                else
                {
                    message = new Message(ReadableBuffer.Create(buffer.Array).Preserve(), Format.Text, receiveResult.EndOfMessage);
                }

                while (await _application.Output.WaitToWriteAsync(cancellationToken))
                {
                    if (_application.Output.TryWrite(message))
                    {
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
                while (!cancellationToken.IsCancellationRequested && _application.Input.TryRead(out message))
                {
                    using (message)
                    {
                        await _webSocket.SendAsync(new ArraySegment<byte>(message.Payload.Buffer.ToArray()),
                            message.MessageFormat == Format.Text ? WebSocketMessageType.Text : WebSocketMessageType.Binary, true,
                            cancellationToken);
                    }
                }
            }
        }

        public async Task Connect(string url)
        {
            await Connect(url, "");
        }

        public async Task Connect(string url, string queryString)
        {
            var wsUrl = url.Replace("http", "ws").Replace("https", "ws");
            await _webSocket.ConnectAsync(new Uri(wsUrl + queryString), token);
        }

        public void Dispose()
        {
            _webSocket.CloseAsync(WebSocketCloseStatus.Empty, null, token);
        }
    }
}
