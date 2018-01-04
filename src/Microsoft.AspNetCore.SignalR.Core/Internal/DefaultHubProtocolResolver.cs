// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Microsoft.AspNetCore.SignalR.Internal
{
    public class DefaultHubProtocolResolver : IHubProtocolResolver
    {
        private readonly IOptions<HubOptions> _options;
        private readonly ILogger<DefaultHubProtocolResolver> _logger;
        private readonly IDictionary<string, IHubProtocol> _availableProtocols;

        public DefaultHubProtocolResolver(IOptions<HubOptions> options, IEnumerable<IHubProtocol> availableProtocols, ILogger<DefaultHubProtocolResolver> logger)
        {
            _options = options;
            _logger = logger;
            _availableProtocols = availableProtocols.ToDictionary(p => p.Name);

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                foreach (var protocol in availableProtocols)
                {
                    _logger.LogDebug("Registered SignalR protocol: {ProtocolName}", protocol.Name);
                }
            }
        }

        public IHubProtocol GetProtocol(string protocolName, HubConnectionContext connection)
        {
            if (_availableProtocols.TryGetValue(protocolName, out var protocol))
            {
                _logger.LogDebug("Found protocol implementation for requested protocol: {ProtocolName}", protocolName);
                return protocol;
            }

            throw new NotSupportedException($"The protocol '{protocolName ?? "(null)"}' is not supported.");
        }
    }
}
