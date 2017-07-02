using System;
using System.IO.Pipelines;
using System.Security.Claims;
using System.Threading.Tasks.Channels;
using Microsoft.AspNetCore.Http.Features.Authentication;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Microsoft.AspNetCore.Sockets;

namespace Microsoft.AspNetCore.SignalR
{
    public class HubConnectionContext
    {
        private readonly WritableChannel<byte[]> _output;
        private readonly ConnectionContext _connectionContext;

        public HubConnectionContext()
        {

        }

        public HubConnectionContext(WritableChannel<byte[]> output, ConnectionContext connectionContext)
        {
            _output = output;
            _connectionContext = connectionContext;
        }

        // Used by the HubEndPoint only
        internal IPipeReader Input => _connectionContext.Transport.Reader;

        public virtual string ConnectionId => _connectionContext.ConnectionId;

        public virtual ClaimsPrincipal User => _connectionContext.Features.Get<IHttpAuthenticationFeature>()?.User;

        public virtual ConnectionMetadata Metadata => _connectionContext.Metadata;

        public virtual IHubProtocol Protocol => _connectionContext.Metadata.Get<IHubProtocol>(HubConnectionMetadataNames.HubProtocol);

        public virtual WritableChannel<byte[]> Output => _output;
    }
}
