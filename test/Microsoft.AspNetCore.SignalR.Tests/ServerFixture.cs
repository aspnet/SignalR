// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;


namespace Microsoft.AspNetCore.SignalR.Tests
{
    public class ServerFixture : IDisposable
    {
        private ILoggerFactory _loggerFactory;
        private IWebHost host;
        private const string _baseUrl = "http://localhost:3000";
        private string _webSocketsUrl = _baseUrl.Replace("http","ws");

        public string BaseUrl
        {
            get { return _baseUrl; }
        }

        public string WebSocketsUrl
        {
            get { return _webSocketsUrl; }
        }

        public string ServerProjectName
        {
            get { return "Microsoft.AspNetCore.SignalR.Test.Server"; }
        }

        public ServerFixture()
        {
            _loggerFactory = new LoggerFactory();

            var _verbose = string.Equals(Environment.GetEnvironmentVariable("SIGNALR_TESTS_VERBOSE"), "1");
            if (_verbose)
            {
                _loggerFactory.AddConsole();
            }
            StartServer();
        }

        private void StartServer()
        {
            host = new WebHostBuilder()
                .UseKestrel()
                .UseUrls(_baseUrl)
                .UseContentRoot(Directory.GetCurrentDirectory())
                .UseStartup<Startup>()
                .Build();

            Task.Run(() => host.Start());
            Console.WriteLine("Deploying test server...");
        }

        public void Dispose()
        {
            host.Dispose();
        }
    }

}
