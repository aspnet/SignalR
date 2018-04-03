// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using MsgPack.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Xunit;

namespace Microsoft.AspNetCore.SignalR.Client.Tests
{
    public class HubConnectionBuilderTests
    {
        [Fact]
        public void HubConnectionBuiderThrowsIfConnectionFactoryNotConfigured()
        {
            var ex = Assert.Throws<InvalidOperationException>(() => new HubConnectionBuilder().Build());
            Assert.Equal("Cannot create HubConnection instance. A connection was not configured.", ex.Message);
        }

        [Fact]
        public void WithLoggerFactoryThrowsForNullLoggerFactory()
        {
            Assert.Equal("loggerFactory",
                Assert.Throws<ArgumentNullException>(() => new HubConnectionBuilder().WithLoggerFactory(null)).ParamName);
        }

        [Fact]
        public void WithJsonHubProtocolSetsHubProtocolToJsonWithDefaultOptions()
        {
            var services = new HubConnectionBuilder().WithJsonProtocol().Services;
            var descriptor = services.Single(s => s.ServiceType == typeof(IHubProtocol));

            var actualProtocol = Assert.IsType<JsonHubProtocol>(descriptor.ImplementationInstance);
            Assert.IsType<CamelCasePropertyNamesContractResolver>(actualProtocol.PayloadSerializer.ContractResolver);
        }

        [Fact]
        public void WithJsonHubProtocolSetsHubProtocolToJsonWithProvidedOptions()
        {
            var expectedOptions = new JsonHubProtocolOptions()
            {
                PayloadSerializerSettings = new JsonSerializerSettings()
                {
                    DateFormatString = "JUST A TEST"
                }
            };

            var services = new HubConnectionBuilder().WithJsonProtocol(expectedOptions).Services;
            var descriptor = services.Single(s => s.ServiceType == typeof(IHubProtocol));

            var actualProtocol = Assert.IsType<JsonHubProtocol>(descriptor.ImplementationInstance);
            Assert.Equal("JUST A TEST", actualProtocol.PayloadSerializer.DateFormatString);
        }

        [Fact]
        public void WithMessagePackHubProtocolSetsHubProtocolToMsgPackWithDefaultOptions()
        {
            var services = new HubConnectionBuilder().WithMessagePackProtocol().Services;
            var descriptor = services.Single(s => s.ServiceType == typeof(IHubProtocol));

            var actualProtocol = Assert.IsType<MessagePackHubProtocol>(descriptor.ImplementationInstance);
            Assert.Equal(SerializationMethod.Map, actualProtocol.SerializationContext.SerializationMethod);
        }

        [Fact]
        public void WithMessagePackHubProtocolSetsHubProtocolToMsgPackWithProvidedOptions()
        {
            var expectedOptions = new MessagePackHubProtocolOptions()
            {
                SerializationContext = new SerializationContext()
                {
                    SerializationMethod = SerializationMethod.Array
                }
            };

            var services = new HubConnectionBuilder().WithMessagePackProtocol(expectedOptions).Services;
            var descriptor = services.Single(s => s.ServiceType == typeof(IHubProtocol));

            var actualProtocol = Assert.IsType<MessagePackHubProtocol>(descriptor.ImplementationInstance);
            Assert.Equal(SerializationMethod.Array, actualProtocol.SerializationContext.SerializationMethod);
        }
    }
}
