using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Microsoft.AspNetCore.Sockets.Tests
{
    internal class TestWebSocketConnectionFeature : IHttpWebSocketFeature, IDisposable
    {
        public bool IsWebSocketRequest => true;

        public WebSocketChannel Client { get; private set; }

        public Task<WebSocket> AcceptAsync() => AcceptAsync(new WebSocketAcceptContext());

        public Task<WebSocket> AcceptAsync(WebSocketAcceptContext context)
        {
            var clientToServer = Channel.CreateUnbounded<WebSocketMessage>();
            var serverToClient = Channel.CreateUnbounded<WebSocketMessage>();

            var clientSocket = new WebSocketChannel(serverToClient.Reader, clientToServer.Writer);
            var serverSocket = new WebSocketChannel(clientToServer.Reader, serverToClient.Writer);

            Client = clientSocket;
            return Task.FromResult<WebSocket>(serverSocket);
        }

        public void Dispose()
        {
        }

        public class WebSocketChannel : WebSocket
        {
            private readonly ChannelReader<WebSocketMessage> _input;
            private readonly ChannelWriter<WebSocketMessage> _output;

            private WebSocketCloseStatus? _closeStatus;
            private string _closeStatusDescription;
            private WebSocketState _state;

            public WebSocketChannel(ChannelReader<WebSocketMessage> input, ChannelWriter<WebSocketMessage> output)
            {
                _input = input;
                _output = output;
            }

            public override WebSocketCloseStatus? CloseStatus => _closeStatus;

            public override string CloseStatusDescription => _closeStatusDescription;

            public override WebSocketState State => _state;

            public override string SubProtocol => null;

            public override void Abort()
            {
                _output.TryComplete(new OperationCanceledException());
                _state = WebSocketState.Aborted;
            }

            public void SendAbort()
            {
                _output.TryComplete(new WebSocketException(WebSocketError.ConnectionClosedPrematurely));
            }

            public override async Task CloseAsync(WebSocketCloseStatus closeStatus, string statusDescription, CancellationToken cancellationToken)
            {
                await SendMessageAsync(new WebSocketMessage
                {
                    CloseStatus = closeStatus,
                    CloseStatusDescription = statusDescription,
                    MessageType = WebSocketMessageType.Close,
                },
                cancellationToken);

                _state = WebSocketState.CloseSent;

                _output.TryComplete();
            }

            public override async Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string statusDescription, CancellationToken cancellationToken)
            {
                await SendMessageAsync(new WebSocketMessage
                {
                    CloseStatus = closeStatus,
                    CloseStatusDescription = statusDescription,
                    MessageType = WebSocketMessageType.Close,
                },
                cancellationToken);

                _state = WebSocketState.CloseSent;

                _output.TryComplete();
            }

            public override void Dispose()
            {
                _state = WebSocketState.Closed;
                _output.TryComplete();
            }

            public override async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
            {
                try
                {
                    await _input.WaitToReadAsync();

                    if (_input.TryRead(out var message))
                    {
                        if (message.MessageType == WebSocketMessageType.Close)
                        {
                            _state = WebSocketState.CloseReceived;
                            _closeStatus = message.CloseStatus;
                            _closeStatusDescription = message.CloseStatusDescription;
                            return new WebSocketReceiveResult(0, WebSocketMessageType.Close, true, message.CloseStatus, message.CloseStatusDescription);
                        }

                        // REVIEW: This assumes the buffer passed in is > the buffer received
                        Buffer.BlockCopy(message.Buffer, 0, buffer.Array, buffer.Offset, message.Buffer.Length);

                        return new WebSocketReceiveResult(message.Buffer.Length, message.MessageType, message.EndOfMessage);
                    }
                }
                catch (WebSocketException ex)
                {
                    switch (ex.WebSocketErrorCode)
                    {
                        case WebSocketError.ConnectionClosedPrematurely:
                            _state = WebSocketState.Aborted;
                            break;
                    }

                    throw;
                }

                throw new InvalidOperationException("Unexpected close");
            }

            public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
            {
                var copy = new byte[buffer.Count];
                Buffer.BlockCopy(buffer.Array, buffer.Offset, copy, 0, buffer.Count);
                return SendMessageAsync(new WebSocketMessage
                {
                    Buffer = copy,
                    MessageType = messageType,
                    EndOfMessage = endOfMessage
                },
                cancellationToken);
            }

            public async Task<WebSocketConnectionSummary> ExecuteAndCaptureFramesAsync()
            {
                var frames = new List<WebSocketMessage>();
                while (await _input.WaitToReadAsync())
                {
                    while (_input.TryRead(out var message))
                    {
                        if (message.MessageType == WebSocketMessageType.Close)
                        {
                            _state = WebSocketState.CloseReceived;
                            _closeStatus = message.CloseStatus;
                            _closeStatusDescription = message.CloseStatusDescription;
                            return new WebSocketConnectionSummary(frames, new WebSocketReceiveResult(0, message.MessageType, message.EndOfMessage, message.CloseStatus, message.CloseStatusDescription));
                        }

                        frames.Add(message);
                    }
                }
                _state = WebSocketState.Closed;
                _closeStatus = WebSocketCloseStatus.InternalServerError;
                return new WebSocketConnectionSummary(frames, new WebSocketReceiveResult(0, WebSocketMessageType.Close, endOfMessage: true, closeStatus: WebSocketCloseStatus.InternalServerError, closeStatusDescription: ""));
            }

            private async Task SendMessageAsync(WebSocketMessage webSocketMessage, CancellationToken cancellationToken)
            {
                while (await _output.WaitToWriteAsync(cancellationToken))
                {
                    if (_output.TryWrite(webSocketMessage))
                    {
                        break;
                    }
                }
            }
        }

        public class WebSocketConnectionSummary
        {
            public IList<WebSocketMessage> Received { get; }
            public WebSocketReceiveResult CloseResult { get; }

            public WebSocketConnectionSummary(IList<WebSocketMessage> received, WebSocketReceiveResult closeResult)
            {
                Received = received;
                CloseResult = closeResult;
            }
        }

        public class WebSocketMessage
        {
            public byte[] Buffer { get; set; }
            public WebSocketMessageType MessageType { get; set; }
            public bool EndOfMessage { get; set; }
            public WebSocketCloseStatus? CloseStatus { get; set; }
            public string CloseStatusDescription { get; set; }
        }
    }
}
