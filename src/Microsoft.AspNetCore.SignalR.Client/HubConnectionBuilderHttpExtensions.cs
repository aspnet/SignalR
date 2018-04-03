// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.ObjectModel;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Http.Connections.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.AspNetCore.SignalR.Client
{
    public static class HubConnectionBuilderHttpExtensions
    {
        public static IHubConnectionBuilder WithHttpConnection(this IHubConnectionBuilder hubConnectionBuilder, string url, Action<HttpConnectionOptions> configureHttpConnection = null)
        {
            hubConnectionBuilder.WithHttpConnection(new Uri(url), configureHttpConnection);
            return hubConnectionBuilder;
        }

        public static IHubConnectionBuilder WithHttpConnection(this IHubConnectionBuilder hubConnectionBuilder, Uri url, Action<HttpConnectionOptions> configureHttpConnection = null)
        {
            HttpConnectionOptions options = new HttpConnectionOptions();
            options.Url = url;

            configureHttpConnection?.Invoke(options);

            var httpOptions = new HttpOptions
            {
                HttpMessageHandlerFactory = options.MessageHandlerFactory,
                Headers = options._headers != null ? new ReadOnlyDictionary<string, string>(options._headers) : null,
                AccessTokenFactory = options.AccessTokenFactory,
                WebSocketOptions = options.WebSocketOptions,
                Cookies = options._cookies,
                Proxy = options.Proxy,
                UseDefaultCredentials = options.UseDefaultCredentials,
                ClientCertificates = options._clientCertificates,
                Credentials = options.Credentials,
            };

            Func<IConnection> createConnection = () => new HttpConnection(options.Url,
                options.Transport ?? TransportType.All,
                null, // TODO: Pass in logger factory
                httpOptions);

            hubConnectionBuilder.Services.AddSingleton(createConnection);
            return hubConnectionBuilder;
        }
    }
}
