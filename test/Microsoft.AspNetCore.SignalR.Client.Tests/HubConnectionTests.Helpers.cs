using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.SignalR.Client.Tests
{
    public partial class HubConnectionTests
    {
        private static HubConnection CreateHubConnection(TestConnection connection, IHubProtocol protocol = null)
        {
            Func<IConnection> connectionFactory = () => connection;

            var builder = new HubConnectionBuilder();
            builder.Services.AddSingleton(connectionFactory);
            if (protocol != null)
            {
                builder.WithHubProtocol(protocol);
            }

            return builder.Build();
        }
    }
}
