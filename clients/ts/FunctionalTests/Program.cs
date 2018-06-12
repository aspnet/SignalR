// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
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
                switch (args[0])
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
                    factory.AddFilter("Console", level => level >= LogLevel.Information);
                    factory.AddDebug();
                })
                .UseKestrel()
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
