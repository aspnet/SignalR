using System;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace SignalRSamples
{
    public static class SignalRV2RouteBuilderExtensions
    {
        public static IRouteBuilder MapSignalRV2Connections(this IRouteBuilder routes, PathString path, Action<IConnectionBuilder> configure)
        {
            var connectionBuilder = new ConnectionBuilder(routes.ServiceProvider);
            configure(connectionBuilder);
            var connectionDelegate = connectionBuilder.Build();

            var dispatcher = routes.ServiceProvider.GetRequiredService<SignalRV2Dispatcher>();
            var options = new HttpConnectionDispatcherOptions();

            routes.MapRoute(path + "/negotiate", c => dispatcher.ExecuteNegotiateAsync(c));
            routes.MapRoute(path + "/abort", c => dispatcher.ExecuteAbortAsync(c));
            routes.MapRoute(path + "/connect", c => dispatcher.ExecuteConnectAsync(c, options, connectionDelegate));
            routes.MapRoute(path + "/send", c => dispatcher.ExecuteSendAsync(c));
            routes.MapRoute(path + "/start", c => dispatcher.ExecuteStartAsync(c));
            routes.MapRoute(path + "/reconnect", c => dispatcher.ExecuteReconnectAsync(c, options, connectionDelegate));
            routes.MapRoute(path + "/ping", c => dispatcher.ExecutePingAsync(c));

            return routes;
        }
    }
}