using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Sockets;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class SocketsDependencyInjectionExtensions
    {
        public static IServiceCollection AddSockets(this IServiceCollection services)
        {
            services.AddRouting();

            // This preserves the TryAdd* semantics
            if (!services.Any(s => s.ServiceType == typeof(ConnectionManager)))
            {
                // NOTE: This is a limitation of the DI system. There's no other way to
                // resolve the same instance with 2 different interfaces today.
                var connectionManager = new ConnectionManager();
                services.AddSingleton(connectionManager);
                services.AddSingleton<IApplicationLifetimeEvents>(connectionManager);
            }

            services.TryAddSingleton<PipelineFactory>();
            services.TryAddSingleton<HttpConnectionDispatcher>();
            return services;
        }
    }
}
