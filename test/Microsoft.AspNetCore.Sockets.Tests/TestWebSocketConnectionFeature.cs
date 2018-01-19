using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Sockets.Tests
{
    internal class TestWebSocketConnectionFeature : IHttpWebSocketFeature, IDisposable
    {
        private readonly ILoggerFactory _loggerFactory;

        public bool IsWebSocketRequest => true;

        public WebSocketChannel Client { get; private set; }

        public TestWebSocketConnectionFeature(ILoggerFactory loggerFactory)
        {
            _loggerFactory = loggerFactory;
        }

        public Task<WebSocket> AcceptAsync() => AcceptAsync(new WebSocketAcceptContext());

        public Task<WebSocket> AcceptAsync(WebSocketAcceptContext context)
        {
            var clientToServer = Channel.CreateUnbounded<WebSocketMessage>();
            var serverToClient = Channel.CreateUnbounded<WebSocketMessage>();

            var clientSocket = new WebSocketChannel(serverToClient.Reader, clientToServer.Writer, _loggerFactory.CreateLogger($"{typeof(WebSocketChannel).FullName}:Client"));
            var serverSocket = new WebSocketChannel(clientToServer.Reader, serverToClient.Writer, _loggerFactory.CreateLogger($"{typeof(WebSocketChannel).FullName}:Server"));

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
            private readonly ILogger _logger;
            private WebSocketCloseStatus? _closeStatus;
            private string _closeStatusDescription;
            private WebSocketState _state;

            public WebSocketChannel(ChannelReader<WebSocketMessage> input, ChannelWriter<WebSocketMessage> output, ILogger logger)
            {
                _input = input;
                _output = output;
                _logger = logger;
            }

            public override WebSocketCloseStatus? CloseStatus => _closeStatus;

            public override string CloseStatusDescription => _closeStatusDescription;

            public override WebSocketState State => _state;

            public override string SubProtocol => null;

            public override void Abort()
            {
                _logger.LogDebug("Aborting socket.");
                _output.TryComplete(new OperationCanceledException());
                _state = WebSocketState.Aborted;
            }

            public void SendAbort()
            {
                _logger.LogDebug("Terminating connection abnormally.");
                _output.TryComplete(new WebSocketException(WebSocketError.ConnectionClosedPrematurely));
            }

            public override async Task CloseAsync(WebSocketCloseStatus closeStatus, string statusDescription, CancellationToken cancellationToken)
            {
                CloseOutputAsync(closeStatus, statusDescription, cancellationToken);
            }

            public override async Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string statusDescription, CancellationToken cancellationToken)
            {
                _logger.LogDebug("Sending close frame with status {closeStatus} {closeStatusDescription}.", closeStatus, statusDescription);
                await SendMessageAsync(new WebSocketMessage
                {
                    CloseStatus = closeStatus,
                    CloseStatusDescription = statusDescription,
                    MessageType = WebSocketMessageType.Close,
                },
                cancellationToken);

                _state = WebSocketState.CloseSent;

                _output.TryComplete();
                _logger.LogDebug("Close frame sent.");
            }

            public override void Dispose()
            {
                _logger.LogDebug("Disposing socket.");
                _state = WebSocketState.Closed;
                _output.TryComplete();
            }

            public override async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
            {
                try
                {
                    _logger.LogDebug("Waiting for a message to arrive.");
                    await _input.WaitToReadAsync(cancellationToken);

                    if (_input.TryRead(out var message))
                    {
                        if (message.MessageType == WebSocketMessageType.Close)
                        {
                            _state = WebSocketState.CloseReceived;
                            _closeStatus = message.CloseStatus;
                            _closeStatusDescription = message.CloseStatusDescription;
                            _logger.LogDebug("Received {frameType} frame with close status {closeStatus} {closeStatusDescription}.", message.MessageType, message.CloseStatus, message.CloseStatusDescription);
                            return new WebSocketReceiveResult(0, WebSocketMessageType.Close, true, message.CloseStatus, message.CloseStatusDescription);
                        }
                        _logger.LogDebug("Received {frameType} frame with {payloadSize} bytes of payload.", message.MessageType, message.Buffer.Length);

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

            public async Task<WebSocketConnectionSummary> ExecuteAndCaptureFramesAsync(CancellationToken cancellationToken)
            {
                _logger.LogDebug("Collecting frames sent to socket");
                var frames = new List<WebSocketMessage>();
                while (await _input.WaitToReadAsync(cancellationToken))
                {
                    while (_input.TryRead(out var message))
                    {
                        if (message.MessageType == WebSocketMessageType.Close)
                        {
                            _state = WebSocketState.CloseReceived;
                            _closeStatus = message.CloseStatus;
                            _closeStatusDescription = message.CloseStatusDescription;
                            _logger.LogDebug("Received {frameType} frame with close status {closeStatus} {closeStatusDescription}.", message.MessageType, message.CloseStatus, message.CloseStatusDescription);
                            return new WebSocketConnectionSummary(frames, new WebSocketReceiveResult(0, message.MessageType, message.EndOfMessage, message.CloseStatus, message.CloseStatusDescription));
                        }

                        _logger.LogDebug("Collected {frameType} frame with {payloadLength} bytes of payload.", message.MessageType, message.Buffer.Length);
                        frames.Add(message);
                    }
                }

                _state = WebSocketState.Closed;
                _closeStatus = WebSocketCloseStatus.InternalServerError;
                _logger.LogDebug(_input.Completion.Exception, "Socket terminated abnormally with exception.");
                return new WebSocketConnectionSummary(frames, new WebSocketReceiveResult(0, WebSocketMessageType.Close, endOfMessage: true, closeStatus: WebSocketCloseStatus.InternalServerError, closeStatusDescription: ""));
            }

            private async Task SendMessageAsync(WebSocketMessage webSocketMessage, CancellationToken cancellationToken)
            {
                _logger.LogDebug("Sending {payloadLength} byte {frameType} frame.", webSocketMessage.Buffer.Length, webSocketMessage.MessageType);
                while (await _output.WaitToWriteAsync(cancellationToken))
                {
                    if (_output.TryWrite(webSocketMessage))
                    {
                        _logger.LogDebug("Sent frame.");
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
