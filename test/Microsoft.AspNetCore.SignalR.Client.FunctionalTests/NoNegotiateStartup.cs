// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.AspNetCore.SignalR.Client.FunctionalTests
{
    public class NoNegotiateStartup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddSignalR(options => options.Protocol = new MessagePackHubProtocol());
        }

        public void Configure(IApplicationBuilder app)
        {
            app.UseSignalR(routes =>
            {
                routes.MapHub<TestHub>("default");
            });
        }
    }
}
