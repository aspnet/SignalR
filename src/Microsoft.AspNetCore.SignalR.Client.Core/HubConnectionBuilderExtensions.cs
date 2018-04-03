// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.AspNetCore.SignalR.Client
{
    public static class HubConnectionBuilderExtensions
    {
        public static IHubConnectionBuilder WithHubProtocol(this IHubConnectionBuilder hubConnectionBuilder, IHubProtocol hubProtocol)
        {
            hubConnectionBuilder.Services.AddSingleton(hubProtocol);
            return hubConnectionBuilder;
        }

        public static IHubConnectionBuilder WithJsonProtocol(this IHubConnectionBuilder hubConnectionBuilder)
        {
            return hubConnectionBuilder.WithHubProtocol(new JsonHubProtocol());
        }

        public static IHubConnectionBuilder WithJsonProtocol(this IHubConnectionBuilder hubConnectionBuilder, JsonHubProtocolOptions options)
        {
            return hubConnectionBuilder.WithHubProtocol(new JsonHubProtocol(Options.Create(options)));
        }

        public static IHubConnectionBuilder WithLoggerFactory(this IHubConnectionBuilder hubConnectionBuilder, ILoggerFactory loggerFactory)
        {
            if (loggerFactory == null)
            {
                throw new ArgumentNullException(nameof(loggerFactory));
            }

            hubConnectionBuilder.Services.AddSingleton(loggerFactory);
            return hubConnectionBuilder;
        }

        public static IHubConnectionBuilder WithLogger(this IHubConnectionBuilder hubConnectionBuilder, Action<ILoggerFactory> configureLogging)
        {
            var loggerFactory = new LoggerFactory();
            configureLogging(loggerFactory);
            return hubConnectionBuilder.WithLoggerFactory(loggerFactory);
        }
    }
}