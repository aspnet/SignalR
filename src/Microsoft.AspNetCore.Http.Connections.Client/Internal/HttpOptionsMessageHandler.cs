// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.Http.Connections.Client.Internal
{
    public class HttpOptionsMessageHandler : DelegatingHandler
    {
        private readonly HttpOptions _httpOptions;

        public HttpOptionsMessageHandler(HttpMessageHandler inner, HttpOptions options) : base(inner)
        {
            _httpOptions = options;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_httpOptions?.Headers != null)
            {
                foreach (var header in _httpOptions.Headers)
                {
                    request.Headers.Add(header.Key, header.Value);
                }
            }

            request.Headers.UserAgent.Add(Constants.UserAgentHeader);

            if (_httpOptions?.AccessTokenFactory != null)
            {
                request.Headers.Add("Authorization", $"Bearer {_httpOptions.AccessTokenFactory()}");
            }

            return base.SendAsync(request, cancellationToken);
        }
    }
}
