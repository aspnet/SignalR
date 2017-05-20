using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text;
using System.Threading;
using Microsoft.AspNetCore.Http.Features;

namespace Microsoft.AspNetCore.Sockets
{
    public abstract class ConnectionContext : IDisposable
    {
        public abstract string ConnectionId { get; }

        public abstract IFeatureCollection Features { get; }

        public abstract ClaimsPrincipal User { get; set; }

        // REVIEW: Should this be changed to items
        public abstract ConnectionMetadata Metadata { get; }

        // TEMPORARY
        public abstract IChannelConnection<Message> Transport { get; }

        // TEMPORARY
        public void Dispose()
        {
            Transport?.Dispose();
        }
    }
}
