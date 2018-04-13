using Microsoft.AspNetCore.SignalR.Protocol;
using Microsoft.AspNetCore.SignalR.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.AspNetCore.SignalR.Client.Tests
{
    public partial class HubConnectionTests
    {
        private static HubConnection CreateHubConnection(TestConnection connection, IHubProtocol protocol = null)
        {
            var builder = new HubConnectionBuilder();

            DelegateConnectionFactory delegateConnectionFactory = new DelegateConnectionFactory(async format =>
            {
                await connection.StartAsync(format);
                return connection;
            });
            builder.Services.AddSingleton<IConnectionFactory>(delegateConnectionFactory);

            if (protocol != null)
            {
                builder.Services.AddSingleton(protocol);
            }

            return builder.Build();
        }
    }
}
