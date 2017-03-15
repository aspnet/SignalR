using System;
using System.Buffers;
using System.Buffers.Pools;
using System.IO.Pipelines;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebSockets.Internal;
using Microsoft.Extensions.WebSockets.Internal;

namespace Microsoft.AspNetCore.Sockets.Tests
{
    internal class TestWebSocketConnectionFeature : IHttpWebSocketConnectionFeature
    {
        public bool IsWebSocketRequest => true;

        public WebSocketConnection Client { get; private set; }

        public ValueTask<IWebSocketConnection> AcceptWebSocketConnectionAsync(WebSocketAcceptContext context)
        {
            var pipelineFactory = new PipeFactory(ManagedBufferPool.Shared);
            var clientToServer = pipelineFactory.Create();
            var serverToClient = pipelineFactory.Create();

            var clientSocket = new WebSocketConnection(serverToClient.Reader, clientToServer.Writer);
            var serverSocket = new WebSocketConnection(clientToServer.Reader, serverToClient.Writer);

            Client = clientSocket;
            return new ValueTask<IWebSocketConnection>(serverSocket);
        }
    }
}