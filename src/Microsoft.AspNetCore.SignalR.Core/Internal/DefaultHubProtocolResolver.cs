// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Microsoft.AspNetCore.SignalR.Internal
{
    public class DefaultHubProtocolResolver : IHubProtocolResolver
    {
        private readonly ILogger<DefaultHubProtocolResolver> _logger;
        private readonly Dictionary<string, IHubProtocol> _availableProtocols;

        public DefaultHubProtocolResolver(IEnumerable<IHubProtocol> availableProtocols, ILogger<DefaultHubProtocolResolver> logger)
        {
            _logger = logger ?? NullLogger<DefaultHubProtocolResolver>.Instance;
            _availableProtocols = new Dictionary<string, IHubProtocol>(StringComparer.OrdinalIgnoreCase);

            foreach (var protocol in availableProtocols)
            {
                if (_availableProtocols.ContainsKey(protocol.Name))
                {
                    throw new InvalidOperationException($"Multiple Hub Protocols with the name '{protocol.Name}' were registered.");
                }
                Log.RegisteredSignalRProtocol(_logger, protocol.Name, protocol.GetType());
                _availableProtocols.Add(protocol.Name, protocol);
            }
        }

        public IHubProtocol GetProtocol(string protocolName, IList<string> supportedProtocols, HubConnectionContext connection)
        {
            protocolName = protocolName ?? throw new ArgumentNullException(nameof(protocolName));

            if (_availableProtocols.TryGetValue(protocolName, out var protocol) && (supportedProtocols == null || supportedProtocols.Contains(protocolName, StringComparer.OrdinalIgnoreCase)))
            {
                Log.FoundImplementationForProtocol(_logger, protocolName);
                return protocol;
            }

            throw new NotSupportedException($"The protocol '{protocolName ?? "(null)"}' is not supported.");
        }

        private static class Log
        {
            // Category: DefaultHubProtocolResolver
            private static readonly Action<ILogger, string, Type, Exception> _registeredSignalRProtocol =
                LoggerMessage.Define<string, Type>(LogLevel.Debug, new EventId(1, "RegisteredSignalRProtocol"), "Registered SignalR Protocol: {ProtocolName}, implemented by {ImplementationType}.");

            private static readonly Action<ILogger, string, Exception> _foundImplementationForProtocol =
                LoggerMessage.Define<string>(LogLevel.Debug, new EventId(2, "FoundImplementationForProtocol"), "Found protocol implementation for requested protocol: {ProtocolName}.");

            public static void RegisteredSignalRProtocol(ILogger logger, string protocolName, Type implementationType)
            {
                _registeredSignalRProtocol(logger, protocolName, implementationType, null);
            }

            public static void FoundImplementationForProtocol(ILogger logger, string protocolName)
            {
                _foundImplementationForProtocol(logger, protocolName, null);
            }
        }
    }
}
