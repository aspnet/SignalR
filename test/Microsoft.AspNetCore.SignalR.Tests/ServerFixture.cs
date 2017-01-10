using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.IntegrationTesting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.PlatformAbstractions;
using System.Linq;

namespace Microsoft.AspNetCore.SignalR.Tests
{
    public class ServerFixture : IDisposable
    {
        private readonly bool _verbose;

        private Lazy<string> _baseUrl;
        private object _lock = new object();
        private ILoggerFactory _loggerFactory;
        private IApplicationDeployer _deployer;

        public string BaseUrl => _baseUrl.Value;

        public string ServerProjectName
        {
            get { return "Microsoft.AspNetCore.SignalR.Test.Server"; }
        }

        public ServerFixture()
        {
            _loggerFactory = new LoggerFactory();

            _verbose = string.Equals(Environment.GetEnvironmentVariable("SIGNALR_TESTS_VERBOSE"), "1");
            if (_verbose)
            {
                _loggerFactory.AddConsole();
            }

            _baseUrl = new Lazy<string>(() => Deploy().Result);
        }

        private async Task<string> Deploy()
        {
            var url = Environment.GetEnvironmentVariable("SIGNALR_TESTS_URL");
            if (!string.IsNullOrEmpty(url))
            {
                return url;
            }

            Console.WriteLine("Deploying test server...");

            var parameters = new DeploymentParameters(
                applicationPath: GetApplicationPath(ServerProjectName),
                serverType: ServerType.Kestrel,
                runtimeFlavor: RuntimeFlavor.CoreClr,
                runtimeArchitecture: RuntimeArchitecture.x64);
            _deployer = ApplicationDeployerFactory.Create(parameters, _loggerFactory.CreateLogger("Deployment"));
            var result = _deployer.Deploy();

            // Ensure it's working
            var client = new HttpClient();
            var logger = _loggerFactory.CreateLogger("Connection");
            var resp = await RetryHelper.RetryRequest(
                () => client.GetAsync(result.ApplicationBaseUri), logger, result.HostShutdownToken).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();

            Console.WriteLine("Test server ready. Running tests...");
            return result.ApplicationBaseUri;
        }

        private static string GetApplicationPath(string projectName)
        {
            return Path.Combine(GetSolutionDir(), "test", projectName);
        }

        public void Dispose()
        {
            if (_deployer != null)
            {
                _deployer.Dispose();
                _deployer = null;
            }
        }

        public static string GetSolutionDir(string slnFileName = null)
        {
            var applicationBasePath = PlatformServices.Default.Application.ApplicationBasePath;

            var directoryInfo = new DirectoryInfo(applicationBasePath);
            do
            {
                if (string.IsNullOrEmpty(slnFileName))
                {
                    if (directoryInfo.EnumerateFiles("*.sln").Any())
                    {
                        return directoryInfo.FullName;
                    }
                }
                else
                {
                    if (File.Exists(Path.Combine(directoryInfo.FullName, slnFileName)))
                    {
                        return directoryInfo.FullName;
                    }
                }

                directoryInfo = directoryInfo.Parent;
            }
            while (directoryInfo.Parent != null);

            throw new InvalidOperationException($"Solution root could not be found using {applicationBasePath}");
        }
    }
}