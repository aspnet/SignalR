// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Microsoft.AspNetCore.Sockets.Internal.Transports
{
    public interface IHttpTransport
    {
        /// <summary>
        /// Executes the transport
        /// </summary>
        /// <param name="connection"></param>
        /// <param name="context"></param>
        /// <param name="token"></param>
        /// <returns>A <see cref="Task"/> that completes when the transport has finished processing</returns>
        Task ProcessRequestAsync(ConnectionContext connection, HttpContext context, CancellationToken token);
    }
}
