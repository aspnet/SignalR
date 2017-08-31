// Copyright (c) .NET Foundation. All rights reserved.

// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.ComponentModel;
using System.Net.Http;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Microsoft.AspNetCore.Sockets;
using Microsoft.AspNetCore.Sockets.Client;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Microsoft.AspNetCore.SignalR.Client
{
    public class HubConnectionBuilder
    {
        private readonly Uri _url;
        private ILoggerFactory _loggerFactory;
        private TransportType? _transportType;
        private IHubProtocol _hubProtocol;
        private HttpMessageHandler _httpMessageHandler;

        public HubConnectionBuilder(Uri url)
        {
            _url = url;
        }

        public HubConnectionBuilder WithLogger(ILoggerFactory loggerFactory)
        {
            _loggerFactory = loggerFactory;
            return this;
        }

        public HubConnectionBuilder WithTransportType(TransportType transportType)
        {
            _transportType = transportType;
            return this;
        }

        public HubConnectionBuilder WithHubProtocol(IHubProtocol hubProtocol)
        {
            _hubProtocol = hubProtocol;
            return this;
        }

        public HubConnectionBuilder WithHttpMessageHandler(HttpMessageHandler httpMessageHandler)
        {
            _httpMessageHandler = httpMessageHandler;
            return this;
        }

        public HubConnection Build()
        {
            var httpConnection = new HttpConnection(_url, _transportType ?? TransportType.All, _loggerFactory, _httpMessageHandler);
            return new HubConnection(httpConnection, _hubProtocol ?? new JsonHubProtocol(new JsonSerializer()), _loggerFactory);
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        public override int GetHashCode()
        {
            return base.GetHashCode();
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        public override bool Equals(object obj)
        {
            return base.Equals(obj);
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        public override string ToString()
        {
            return base.ToString();
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        public new Type GetType()
        {
            return base.GetType();
        }
    }
}
