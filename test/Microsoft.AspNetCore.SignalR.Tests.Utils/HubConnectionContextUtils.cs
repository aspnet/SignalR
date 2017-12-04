using System;
using System.Threading.Channels;
using Microsoft.AspNetCore.SignalR.Internal;
using Microsoft.AspNetCore.SignalR.Internal.Encoders;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Microsoft.AspNetCore.Sockets;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.AspNetCore.SignalR.Tests
{
    public static class HubConnectionContextUtils
    {
        public static HubConnectionContext Create(DefaultConnectionContext connection, Channel<HubMessage> replacementOutput = null)
        {
            var context = new HubConnectionContext(connection, TimeSpan.FromSeconds(15), NullLoggerFactory.Instance);
            if (replacementOutput != null)
            {
                context.Output = replacementOutput;
            }
            context.ProtocolReaderWriter = new HubProtocolReaderWriter(new JsonHubProtocol(), new PassThroughEncoder());

            _ = context.StartAsync(null);

            return context;
        }
    }
}
