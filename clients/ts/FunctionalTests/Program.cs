// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace FunctionalTests
{
    public class Program
    {
        public static void Main(string[] args)
        {
            string url = null;
            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--url":
                        i += 1;
                        url = args[i];
                        break;
                }
            }

            var hostBuilder = new WebHostBuilder()
                .ConfigureLogging(factory =>
                {
                    factory.AddConsole(options => options.IncludeScopes = true);
                    factory.AddDebug();
                    factory.SetMinimumLevel(LogLevel.Trace);
                })
                .UseKestrel((builderContext, options) =>
                {
                    options.ConfigureEndpointDefaults(o =>
                    {
                        o.UseConnectionLogging();
                    });
                    options.ConfigureHttpsDefaults(httpsOptions =>
                    {
                        var certPath = Path.Combine(Directory.GetCurrentDirectory(), "testCert.pfx");
                        httpsOptions.ServerCertificate = new X509Certificate2(certPath, "testPassword");
                    });
                })
                .UseContentRoot(Directory.GetCurrentDirectory())
                .UseIISIntegration()
                .UseStartup<Startup>();

            if (!string.IsNullOrEmpty(url))
            {
                Console.WriteLine($"Forcing URL to: {url}");
                hostBuilder.UseUrls(url);
            }

            hostBuilder.Build().Run();
        }
    }
}
