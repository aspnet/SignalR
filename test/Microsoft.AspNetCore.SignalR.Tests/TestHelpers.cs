using System;
using System.IO.Pipelines;
using System.Security.Claims;
using Microsoft.AspNetCore.Sockets;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.AspNetCore.SignalR.Tests
{
    static internal class TestHelpers
    {
        static internal IServiceProvider CreateServiceProvider(Action<ServiceCollection> addServices = null)
        {
            var services = new ServiceCollection();
            services.AddOptions()
                .AddLogging()
                .AddSignalR();

            addServices?.Invoke(services);

            return services.BuildServiceProvider();
        }

        internal class ConnectionWrapper : IDisposable
        {
            private PipelineFactory _factory;
            private HttpConnection _httpConnection;
            private ConnectionManager _connectionManager;

            public Connection Connection;
            public HttpConnection HttpConnection => (HttpConnection)Connection.Channel;

            public ConnectionWrapper(string format = "json")
            {
                _factory = new PipelineFactory();
                _httpConnection = new HttpConnection(_factory);

                _connectionManager = new ConnectionManager();

                Connection = _connectionManager.AddNewConnection(_httpConnection).Connection;
                Connection.Metadata["formatType"] = format;
                Connection.User = new ClaimsPrincipal(new ClaimsIdentity());
            }

            public void Dispose()
            {
                _connectionManager.CloseConnections();
                Connection.Channel.Dispose();
                _httpConnection.Dispose();
                _factory.Dispose();
            }
        }
    }
}
