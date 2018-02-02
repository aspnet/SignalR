// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace Microsoft.AspNetCore.SignalR.Tests
{
    public class ServerFixture<TStartup> : IDisposable
        where TStartup : class
    {
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger _logger;
        private IWebHost _host;
        private IApplicationLifetime _lifetime;
        private readonly IDisposable _logToken;

        public string WebSocketsUrl => Url.Replace("http", "ws");

        public string Url { get; private set; }
        public TestSink StartupLogs { get; }

        public ServerFixture()
        {
            var testLog = AssemblyTestLog.ForAssembly(typeof(TStartup).Assembly);
            _logToken = testLog.StartTestLog(null, $"{nameof(ServerFixture<TStartup>)}_{typeof(TStartup).Name}", out _loggerFactory, "ServerFixture");

            StartupLogs = new TestSink(w => true, s => false);
            _loggerFactory.AddProvider(new TestSinkLoggerProvider(StartupLogs));

            _logger = _loggerFactory.CreateLogger<ServerFixture<TStartup>>();
            StartServer();
        }

        private void StartServer()
        {
            _host = new WebHostBuilder()
                .ConfigureLogging(builder =>
                {
                    builder.AddProvider(new TestSinkLoggerProvider(StartupLogs));
                })
                .UseStartup(typeof(TStartup))
                .UseKestrel()
                // We're using 127.0.0.1 instead of localhost to ensure that we use IPV4 across different OSes
                .UseUrls("http://127.0.0.1:0")
                .UseContentRoot(Directory.GetCurrentDirectory())
                .Build();

            var t = Task.Run(() => _host.Start());
            _logger.LogInformation("Starting test server...");
            _lifetime = _host.Services.GetRequiredService<IApplicationLifetime>();
            if (!_lifetime.ApplicationStarted.WaitHandle.WaitOne(TimeSpan.FromSeconds(5)))
            {
                // t probably faulted
                if (t.IsFaulted)
                {
                    throw t.Exception.InnerException;
                }
                throw new TimeoutException($"Timed out waiting for application to start.{Environment.NewLine}Startup Logs:{Environment.NewLine}{RenderLogs(StartupLogs)}");
            }

            Url = _host.ServerFeatures.Get<IServerAddressesFeature>().Addresses.Single();
            _logger.LogInformation("Test Server started at: {Address}", Url);

            _lifetime.ApplicationStopped.Register(() =>
            {
                _logger.LogInformation("Test server shut down");
                _logToken.Dispose();
            });
        }

        public static string RenderLogs(TestSink sink)
        {
            var builder = new StringBuilder();
            foreach (var write in sink.Writes)
            {
                builder.AppendLine($"{write.LoggerName} {write.LogLevel}: {write.Formatter(write.State, write.Exception)}");
                if (write.Exception != null)
                {
                    var text = write.Exception.ToString();
                    foreach (var line in text.Split(new string[] { Environment.NewLine }, StringSplitOptions.None))
                    {
                        builder.AppendLine("| " + line);
                    }
                }
            }
            return builder.ToString();
        }

        public void Dispose()
        {
            _logger.LogInformation("Shutting down test server");
            _host.Dispose();
            _loggerFactory.Dispose();
        }

        private class TestSinkLoggerProvider : ILoggerProvider
        {
            private TestSink _sink;

            public TestSinkLoggerProvider(TestSink sink)
            {
                _sink = sink;
            }

            public ILogger CreateLogger(string categoryName)
            {
                return new TestLogger(categoryName, _sink, enabled: true);
            }

            public void Dispose()
            {
            }
        }
    }
}
