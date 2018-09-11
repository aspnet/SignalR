// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.Http.Connections.Internal.Transports
{
    public interface IHttpTransport
    {
        /// <summary>
        /// Executes a request on behalf of the transport.
        /// </summary>
        /// <param name="context">An <see cref="HttpContext"/> representing the request.</param>
        /// <param name="token">A <see cref="CancellationToken"/> used to indicate the operation should be canceled.</param>
        /// <returns>A <see cref="Task"/> that completes when the transport has finished processing.</returns>
        Task ProcessRequestAsync(HttpContext context, CancellationToken token);
    }
}
