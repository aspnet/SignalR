using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Sockets.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Sockets.Tests
{
    public static class SocketsTestHost
    {
        public static SocketsTestHost<TestEndPoint> CreateWithDefaultEndPoint()
        {
            return new SocketsTestHost<TestEndPoint>();
        }

        public class TestEndPoint : EndPoint
        {
            public override Task OnConnectedAsync(Connection connection)
            {
                throw new NotImplementedException();
            }
        }
    }

    public class SocketsTestHost<TEndPoint> where TEndPoint : EndPoint
    {
        public ConnectionManager ConnectionManager { get; }
        public HttpConnectionDispatcher Dispatcher { get; }
        public ServiceCollection Services { get; }

        public SocketsTestHost()
        {
            ConnectionManager = new ConnectionManager();
            Dispatcher = new HttpConnectionDispatcher(ConnectionManager, new LoggerFactory());
            Services = new ServiceCollection();
            Services.AddSingleton<TEndPoint>();
        }

        public ConnectionState CreateConnection(string transportName = null)
        {
            var connectionState = ConnectionManager.CreateConnection();
            if (!string.IsNullOrEmpty(transportName))
            {
                connectionState.Connection.Metadata["transport"] = transportName;
            }
            return connectionState;
        }

        public async Task<SocketsRequestResult> ExecuteRequestAsync(string path, string queryString = null, Action<HttpContext> contextConfigurator = null)
        {
            var context = new DefaultHttpContext();
            using (var stream = new MemoryStream())
            {
                context.RequestServices = Services.BuildServiceProvider();
                context.Response.Body = stream;
                context.Request.Path = path;
                if (!string.IsNullOrEmpty(queryString))
                {
                    context.Request.QueryString = new QueryString(queryString);
                }

                contextConfigurator?.Invoke(context);

                await Dispatcher.ExecuteAsync<TEndPoint>("", context);

                await stream.FlushAsync();
                var body = stream.ToArray();

                return new SocketsRequestResult(context, body);
            }
        }
    }
}
