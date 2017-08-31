// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Channels;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Microsoft.AspNetCore.Sockets;
using Microsoft.AspNetCore.Sockets.Client;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.SignalR.Client
{
    // NOTE: This requires making HubConnection methods virtual or making IHubConnection interface
    // to make `On` extension methods work
    public class HttpHubConnection
    {
        private HubConnection _hubConnection;

        public event Func<Task> Connected
        {
            add { _hubConnection.Connected += value; }
            remove { _hubConnection.Connected -= value; }
        }

        public event Func<Exception, Task> Closed
        {
            add { _hubConnection.Closed += value; }
            remove { _hubConnection.Closed -= value; }
        }

        public HttpHubConnection(Uri url, ILoggerFactory loggerFactory = null)
        {
            var httpConnection = new HttpConnection(url, loggerFactory);
            _hubConnection = new HubConnection(httpConnection, loggerFactory);
        }

        public HttpHubConnection(Uri url, TransportType transportType, ILoggerFactory loggerFactory = null)
        {
            var httpConnection = new HttpConnection(url, transportType, loggerFactory);
            _hubConnection = new HubConnection(httpConnection, loggerFactory);
        }

        public HttpHubConnection(Uri url, TransportType transportType, IHubProtocol hubProtocol, ILoggerFactory loggerFactory = null)
        {
            var httpConnection = new HttpConnection(url, transportType, loggerFactory);
            _hubConnection = new HubConnection(httpConnection, hubProtocol, loggerFactory);
        }

        public Task StartAsync() => _hubConnection.StartAsync();

        public Task DisposeAsync() => _hubConnection.DisposeAsync();

        public void On(string methodName, Type[] parameterTypes, Func<object[], Task> handler)
            => _hubConnection.On(methodName, parameterTypes, handler);


        public ReadableChannel<object> Stream(string methodName, Type returnType, CancellationToken cancellationToken, params object[] args)
            => _hubConnection.Stream(methodName, returnType, cancellationToken, args);

        public Task<object> InvokeAsync(string methodName, Type returnType, CancellationToken cancellationToken, params object[] args)
            => _hubConnection.InvokeAsync(methodName, returnType, cancellationToken, args);

        public Task SendAsync(string methodName, CancellationToken cancellationToken, params object[] args)
            => _hubConnection.SendAsync(methodName, cancellationToken, args);
    }
}
