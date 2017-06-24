using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Sockets.Client;
using StackExchange.Redis;

namespace Microsoft.AspNetCore.SignalR.Redis
{
    public class RedisConnection : IConnection
    {
        public event Action Connected;
        public event Action<byte[]> Received;
        public event Action<Exception> Closed;

        private readonly ConfigurationOptions _configurationOptions;
        private readonly string _channel;
        private ISubscriber _subscriber;
        private ConnectionMultiplexer _connection;

        public RedisConnection(string channel) : this(new ConfigurationOptions(), channel)
        {

        }

        public RedisConnection(ConfigurationOptions configurationOptions, string channel)
        {
            if (configurationOptions.EndPoints.Count == 0)
            {
                // REVIEW: Should we do this?
                if (configurationOptions.EndPoints.Count == 0)
                {
                    configurationOptions.EndPoints.Add(IPAddress.Loopback, 0);
                    configurationOptions.SetDefaultPorts();
                }
            }

            _configurationOptions = configurationOptions;
            _channel = channel;
        }

        public Task DisposeAsync()
        {
            _connection?.Dispose();

            Closed?.Invoke(null);

            return Task.CompletedTask;
        }

        public Task SendAsync(byte[] data, CancellationToken cancellationToken)
        {
            return _subscriber.PublishAsync(_channel, data);
        }

        public async Task StartAsync()
        {
            _connection = await ConnectionMultiplexer.ConnectAsync(_configurationOptions);
            _connection.ConnectionFailed += OnConnectionFailed;
            _subscriber = _connection.GetSubscriber();

            await _subscriber.SubscribeAsync(_channel, OnMessage);

            Connected?.Invoke();
        }

        private void OnConnectionFailed(object sender, ConnectionFailedEventArgs e)
        {
            Closed?.Invoke(e.Exception);
        }

        private void OnMessage(RedisChannel channel, RedisValue data)
        {
            Received?.Invoke(data);
        }
    }
}
