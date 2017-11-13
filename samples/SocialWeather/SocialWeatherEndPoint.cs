// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Sockets;
using Microsoft.Extensions.Logging;

namespace SocialWeather
{
    public class SocialWeatherEndPoint : EndPoint
    {
        private readonly PersistentConnectionLifeTimeManager _lifetimeManager;
        private readonly FormatterResolver _formatterResolver;
        private readonly ILogger<SocialWeatherEndPoint> _logger;

        public SocialWeatherEndPoint(PersistentConnectionLifeTimeManager lifetimeManager,
            FormatterResolver formatterResolver, ILogger<SocialWeatherEndPoint> logger)
        {
            _lifetimeManager = lifetimeManager;
            _formatterResolver = formatterResolver;
            _logger = logger;
        }

        public async override Task OnConnectedAsync(ConnectionContext connection)
        {
            _lifetimeManager.OnConnectedAsync(connection);
            await ProcessRequests(connection);
            _lifetimeManager.OnDisconnectedAsync(connection);
        }

        public async Task ProcessRequests(ConnectionContext connection)
        {
            var formatter = _formatterResolver.GetFormatter<WeatherReport>(
                (string)connection.Metadata["formatType"]);

            while (await connection.Transport.Reader.WaitToReadAsync())
            {
                if (connection.Transport.Reader.TryRead(out var buffer))
                {
                    var stream = new MemoryStream();
                    await stream.WriteAsync(buffer, 0, buffer.Length);
                    stream.Position = 0;
                    var weatherReport = await formatter.ReadAsync(stream);
                    await _lifetimeManager.SendToAllAsync(weatherReport);
                }
            }
        }
    }
}
