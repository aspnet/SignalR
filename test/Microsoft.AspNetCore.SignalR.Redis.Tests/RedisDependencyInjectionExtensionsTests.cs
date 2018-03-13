using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.AspNetCore.SignalR.Redis.Tests
{
    public class RedisDependencyInjectionExtensionsTests
    {
        // No need to go too deep with these tests, or we're just testing StackExchange.Redis again :). It's the one doing the parsing.
        [Theory]
        [InlineData("testredis.example.com", "testredis.example.com", 0, null, false)]
        [InlineData("testredis.example.com:6380,ssl=True", "testredis.example.com", 6380, null, true)]
        [InlineData("testredis.example.com:6380,password=hunter2,ssl=True", "testredis.example.com", 6380, "hunter2", true)]
        public void AddRedisWithConnectionStringProperlyParsesOptions(string connectionString, string host, int port, string password, bool useSsl)
        {
            var services = new ServiceCollection();
            services.AddSignalR().AddRedis(connectionString);
            var provider = services.BuildServiceProvider();

            var options = provider.GetService<IOptions<RedisOptions>>();
            Assert.NotNull(options.Value);
            Assert.NotNull(options.Value.Options);
            Assert.Equal(password, options.Value.Options.Password);
            Assert.Collection(options.Value.Options.EndPoints,
                ep =>
                {
                    var dnsep = Assert.IsType<DnsEndPoint>(ep);
                    Assert.Equal(host, dnsep.Host);
                    Assert.Equal(port, dnsep.Port);
                });
            Assert.Equal(useSsl, options.Value.Options.Ssl);
        }
    }
}
