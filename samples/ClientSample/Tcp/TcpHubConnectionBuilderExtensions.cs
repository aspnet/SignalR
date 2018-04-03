using System;
using System.Net;
using ClientSample;
using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.AspNetCore.SignalR.Client
{
    public static class TcpHubConnectionBuilderExtensions
    {
        public static IHubConnectionBuilder WithEndPoint(this IHubConnectionBuilder builder, IPEndPoint endPoint)
        {
            Func<IConnection> connectionFactory = () => new TcpConnection(endPoint);
            builder.Services.AddSingleton(connectionFactory);

            return builder;
        }
    }
}
