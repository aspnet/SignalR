// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text;

namespace Microsoft.AspNetCore.SignalR.Test.Server
{
    public class Startup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddSockets();
            services.AddSignalR();
            services.AddSingleton<EchoEndPoint>();
        }

        public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory)
        {
            loggerFactory.AddConsole();

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseStaticFiles();
            app.UseSockets(options => options.MapEndpoint<EchoEndPoint>("/echo"));
            app.UseSignalR(routes =>
            {
                routes.MapHub<TestHub>("/testhub");
            });

            var data = Encoding.UTF8.GetBytes("Server online");
            app.Use(async (context, next) =>
            {
                await context.Response.Body.WriteAsync(data, 0, data.Length);
            });
        }
    }
}
