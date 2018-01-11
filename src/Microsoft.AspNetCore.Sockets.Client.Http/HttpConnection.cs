// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Sockets.Client.Http;
using Microsoft.AspNetCore.Sockets.Client.Http.Internal;
using Microsoft.AspNetCore.Sockets.Client.Internal;
using Microsoft.AspNetCore.Sockets.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.AspNetCore.Sockets.Client
{
    public class HttpConnection : IConnection
    {
        private static readonly TimeSpan HttpClientTimeout = TimeSpan.FromSeconds(120);

        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger _logger;
        private readonly HttpClient _httpClient;
        private readonly HttpOptions _httpOptions;
        private readonly List<ReceiveCallback> _callbacks = new List<ReceiveCallback>();
        private readonly TransportType _requestedTransportType = TransportType.All;
        private readonly ConnectionLogScope _logScope;
        private readonly IDisposable _scopeDisposable;
        private readonly ITransportFactory _transportFactory;

        private HttpConnectionSession _connection;
        private List<Action<Exception>> _closed = new List<Action<Exception>>();
        private bool _disposed;

        public Uri Url { get; }

        public IFeatureCollection Features { get; } = new FeatureCollection();

        public event Action<Exception> Closed
        {
            add
            {
                lock (_closed)
                {
                    _closed.Add(value);
                }
            }
            remove
            {
                lock (_closed)
                {
                    _closed.Remove(value);
                }
            }
        }

        public HttpConnection(Uri url)
            : this(url, TransportType.All)
        { }

        public HttpConnection(Uri url, TransportType transportType)
            : this(url, transportType, loggerFactory: null)
        {
        }

        public HttpConnection(Uri url, ILoggerFactory loggerFactory)
            : this(url, TransportType.All, loggerFactory, httpOptions: null)
        {
        }

        public HttpConnection(Uri url, TransportType transportType, ILoggerFactory loggerFactory)
            : this(url, transportType, loggerFactory, httpOptions: null)
        {
        }

        public HttpConnection(Uri url, TransportType transportType, ILoggerFactory loggerFactory, HttpOptions httpOptions)
        {
            Url = url ?? throw new ArgumentNullException(nameof(url));

            _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
            _logger = _loggerFactory.CreateLogger<HttpConnection>();
            _httpOptions = httpOptions;

            _requestedTransportType = transportType;
            if (_requestedTransportType != TransportType.WebSockets)
            {
                _httpClient = httpOptions?.HttpMessageHandler == null ? new HttpClient() : new HttpClient(httpOptions.HttpMessageHandler);
                _httpClient.Timeout = HttpClientTimeout;
            }

            _transportFactory = new DefaultTransportFactory(transportType, _loggerFactory, _httpClient, httpOptions);
            _logScope = new ConnectionLogScope();
        }

        public HttpConnection(Uri url, ITransportFactory transportFactory, ILoggerFactory loggerFactory, HttpOptions httpOptions)
        {
            Url = url ?? throw new ArgumentNullException(nameof(url));
            _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
            _logger = _loggerFactory.CreateLogger<HttpConnection>();
            _httpOptions = httpOptions;
            _httpClient = _httpOptions?.HttpMessageHandler == null ? new HttpClient() : new HttpClient(_httpOptions?.HttpMessageHandler);
            _httpClient.Timeout = HttpClientTimeout;
            _transportFactory = transportFactory ?? throw new ArgumentNullException(nameof(transportFactory));
            _logScope = new ConnectionLogScope();
            _scopeDisposable = _logger.BeginScope(_logScope);
        }

        // AbortAsync creates a few thread-safety races that we are OK with.
        //  1. If the transport shuts down gracefully after AbortAsync is called but BEFORE _abortException is called, then the
        //     Closed event will not receive the Abort exception. This is OK because technically the transport was shut down gracefully
        //     before it was aborted
        //  2. If the transport is closed gracefully and then AbortAsync is called before it captures the _abortException value
        //     the graceful shutdown could be turned into an abort. However, again, this is an inherent race between two different conditions:
        //     The transport shutting down because the server went away, and the user requesting the Abort
        public async Task AbortAsync(Exception exception) =>
            await AbortAsyncCore(exception ?? throw new ArgumentNullException(nameof(exception))).ForceAsync();

        private Task AbortAsyncCore(Exception exception)
        {
            var connection = _connection;
            if (connection == null || _disposed)
            {
                return Task.CompletedTask;
            }

            return connection.StopAsync(exception);
        }

        public async Task DisposeAsync() => await DisposeAsyncCore().ForceAsync();

        private async Task DisposeAsyncCore()
        {
            if (_disposed == true)
            {
                return;
            }
            _disposed = true;

            var connection = _connection;
            if (connection != null)
            {
                await connection.DisposeAsync();
            }

            _httpClient?.Dispose();
        }

        public IDisposable OnReceived(Func<byte[], object, Task> callback, object state)
        {
            var receiveCallback = new ReceiveCallback(callback, state);
            lock (_callbacks)
            {
                _callbacks.Add(receiveCallback);
            }
            return new Subscription(receiveCallback, _callbacks);
        }

        public async Task SendAsync(byte[] data, CancellationToken cancellationToken = default) =>
            await SendAsyncCore(data, cancellationToken).ForceAsync();

        private Task SendAsyncCore(byte[] data, CancellationToken cancellationToken = default)
        {
            var connection = _connection;
            if (connection == null || _disposed)
            {
                throw new InvalidOperationException(
                    "Cannot send messages when the connection is not in the Connected state.");
            }

            return connection.SendAsync(data, cancellationToken);
        }

        public async Task StartAsync() => await StartAsyncCore().ForceAsync();

        private async Task StartAsyncCore()
        {
            if (_connection != null || _disposed)
            {
                throw new InvalidOperationException(
                    "Cannot start a connection that is not in the Disconnected state.");
            }

            var connection = new HttpConnectionSession(Url, _transportFactory, _requestedTransportType, _logger, _httpClient, _httpOptions, this,
                async buffer =>
                {
                    // Copying the callbacks to avoid concurrency issues
                    ReceiveCallback[] callbackCopies;
                    lock (_callbacks)
                    {
                        callbackCopies = new ReceiveCallback[_callbacks.Count];
                        _callbacks.CopyTo(callbackCopies);
                    }

                    foreach (var callbackObject in callbackCopies)
                    {
                        try
                        {
                            await callbackObject.InvokeAsync(buffer);
                        }
                        catch (Exception ex)
                        {
                            _logger.ExceptionThrownFromCallback(nameof(OnReceived), ex);
                        }
                    }
                },
                ex =>
                {
                    _connection = null;
                    Action<Exception>[] closed;
                    lock (_closed)
                    {
                        closed = _closed.ToArray();
                    }
                    foreach (var callback in closed)
                    {
                        try
                        {
                            callback(ex);
                        }
                        catch { }
                    }
                });

            if (Interlocked.CompareExchange(ref _connection, connection, null) != null)
            {
                // Another StartAsync finished while this one was running
                //throw?
                return;
            }

            try
            {
                await connection.StartAsync();
            }
            catch
            {
                _connection = null;
                throw;
            }
        }

        public async Task StopAsync() => await AbortAsyncCore(exception: null).ForceAsync();
    }

    internal class ReceiveCallback
    {
        private readonly Func<byte[], object, Task> _callback;
        private readonly object _state;

        public ReceiveCallback(Func<byte[], object, Task> callback, object state)
        {
            _callback = callback;
            _state = state;
        }

        public Task InvokeAsync(byte[] data)
        {
            return _callback(data, _state);
        }
    }

    internal class Subscription : IDisposable
    {
        private readonly ReceiveCallback _receiveCallback;
        private readonly List<ReceiveCallback> _callbacks;

        public Subscription(ReceiveCallback callback, List<ReceiveCallback> callbacks)
        {
            _receiveCallback = callback;
            _callbacks = callbacks;
        }

        public void Dispose()
        {
            lock (_callbacks)
            {
                _callbacks.Remove(_receiveCallback);
            }
        }
    }
}
