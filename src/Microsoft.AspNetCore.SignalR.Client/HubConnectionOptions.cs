using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Microsoft.AspNetCore.Sockets;
using Microsoft.AspNetCore.Sockets.Client;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Microsoft.AspNetCore.SignalR.Client
{
    public class HubConnectionOptions
    {
        public Uri Url { get; set; }
        public ILoggerFactory LoggerFactory { get; set; }
        public TransportType TransportType { get; set; } = TransportType.All;
        public IHubProtocol HubProtocol { get; set; }

        public HttpMessageHandler HttpMessageHandler { get; set; }

        public HubConnection Create()
        {
            var httpConnection = new HttpConnection(Url, TransportType, LoggerFactory, HttpMessageHandler);
            return new HubConnection(httpConnection, HubProtocol ?? new JsonHubProtocol(new JsonSerializer()), LoggerFactory);
        }
    }
}
