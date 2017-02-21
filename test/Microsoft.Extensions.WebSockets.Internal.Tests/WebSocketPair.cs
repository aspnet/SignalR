﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO.Pipelines;

namespace Microsoft.Extensions.WebSockets.Internal.Tests
{
    internal class WebSocketPair : IDisposable
    {
        private static readonly WebSocketOptions DefaultServerOptions = new WebSocketOptions().WithAllFramesPassedThrough().WithRandomMasking();
        private static readonly WebSocketOptions DefaultClientOptions = new WebSocketOptions().WithAllFramesPassedThrough();

        private PipeFactory _factory;
        private readonly bool _ownFactory;

        public IPipe ServerToClient { get; }
        public IPipe ClientToServer { get; }

        public IWebSocketConnection ClientSocket { get; }
        public IWebSocketConnection ServerSocket { get; }

        public WebSocketPair(bool ownFactory, PipeFactory factory, IPipe serverToClient, IPipe clientToServer, IWebSocketConnection clientSocket, IWebSocketConnection serverSocket)
        {
            _ownFactory = ownFactory;
            _factory = factory;
            ServerToClient = serverToClient;
            ClientToServer = clientToServer;
            ClientSocket = clientSocket;
            ServerSocket = serverSocket;
        }

        public static WebSocketPair Create() => Create(new PipeFactory(), DefaultServerOptions, DefaultClientOptions, ownFactory: true);
        public static WebSocketPair Create(PipeFactory factory) => Create(factory, DefaultServerOptions, DefaultClientOptions, ownFactory: false);
        public static WebSocketPair Create(WebSocketOptions serverOptions, WebSocketOptions clientOptions) => Create(new PipeFactory(), serverOptions, clientOptions, ownFactory: true);
        public static WebSocketPair Create(PipeFactory factory, WebSocketOptions serverOptions, WebSocketOptions clientOptions) => Create(factory, serverOptions, clientOptions, ownFactory: false);

        private static WebSocketPair Create(PipeFactory factory, WebSocketOptions serverOptions, WebSocketOptions clientOptions, bool ownFactory)
        {
            // Create channels
            var serverToClient = factory.Create();
            var clientToServer = factory.Create();

            var serverSocket = new WebSocketConnection(clientToServer.Reader, serverToClient.Writer, options: serverOptions);
            var clientSocket = new WebSocketConnection(serverToClient.Reader, clientToServer.Writer, options: clientOptions);

            return new WebSocketPair(ownFactory, factory, serverToClient, clientToServer, clientSocket, serverSocket);
        }

        public void Dispose()
        {
            ServerSocket.Dispose();
            ClientSocket.Dispose();

            if (_ownFactory)
            {
                _factory.Dispose();
            }
        }

        public void TerminateFromClient(Exception ex = null)
        {
            ClientToServer.Writer.Complete(ex);
        }
    }
}