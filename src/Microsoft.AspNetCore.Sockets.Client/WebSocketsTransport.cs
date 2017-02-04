// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.IO.Pipelines;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;


namespace Microsoft.AspNetCore.Sockets.Client
{
    public class WebSocketsTransport : ITransport
    {
        private ClientWebSocket _webSocket = new ClientWebSocket();
        private IChannelConnection<Message> _application;
        private ILogger _logger;
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

            while (!cancellationToken.IsCancellationRequested )
            {
                var buffer = new ArraySegment<byte>(new byte[1]);
                var receiveResult = await _webSocket.ReceiveAsync(buffer, cancellationToken);
                var ms = new MemoryStream();
                var message = new Message(ReadableBuffer.Create(buffer.Array).Preserve(), Format.Text, receiveResult.EndOfMessage);

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
