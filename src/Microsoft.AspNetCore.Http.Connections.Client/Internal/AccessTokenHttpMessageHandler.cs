// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.Http.Connections.Client.Internal
{
    internal class AccessTokenHttpMessageHandler : DelegatingHandler
    {
        private readonly HttpConnectionOptions _httpConnectionOptions;

        public AccessTokenHttpMessageHandler(HttpMessageHandler inner, HttpConnectionOptions httpConnectionOptions) : base(inner)
        {
            _httpConnectionOptions = httpConnectionOptions;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var accessToken = (_httpConnectionOptions.AccessTokenProvider != null) ? await _httpConnectionOptions.AccessTokenProvider() : null;

            if (!string.IsNullOrEmpty(accessToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            }

            return await base.SendAsync(request, cancellationToken);
        }
    }
}
