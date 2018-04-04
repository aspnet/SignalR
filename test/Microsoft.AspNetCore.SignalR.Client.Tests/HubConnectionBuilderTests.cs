// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Microsoft.Extensions.DependencyInjection;
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
        public void AddJsonProtocolSetsHubProtocolToJsonWithDefaultOptions()
        {
            var serviceProvider = new HubConnectionBuilder().AddJsonProtocol().Services.BuildServiceProvider();

            var actualProtocol = Assert.IsType<JsonHubProtocol>(serviceProvider.GetService<IHubProtocol>());
            Assert.IsType<CamelCasePropertyNamesContractResolver>(actualProtocol.PayloadSerializer.ContractResolver);
        }

        [Fact]
        public void AddJsonProtocolSetsHubProtocolToJsonWithProvidedOptions()
        {
            var serviceProvider = new HubConnectionBuilder().AddJsonProtocol(options =>
            {
                options.PayloadSerializerSettings = new JsonSerializerSettings
                {
                    DateFormatString = "JUST A TEST"
                };
            }).Services.BuildServiceProvider();

            var actualProtocol = Assert.IsType<JsonHubProtocol>(serviceProvider.GetService<IHubProtocol>());
            Assert.Equal("JUST A TEST", actualProtocol.PayloadSerializer.DateFormatString);
        }

        [Fact]
        public void AddMessagePackProtocolSetsHubProtocolToMsgPackWithDefaultOptions()
        {
            var serviceProvider = new HubConnectionBuilder().AddMessagePackProtocol().Services.BuildServiceProvider();

            var actualProtocol = Assert.IsType<MessagePackHubProtocol>(serviceProvider.GetService<IHubProtocol>());
            Assert.Equal(SerializationMethod.Map, actualProtocol.SerializationContext.SerializationMethod);
        }

        [Fact]
        public void AddMessagePackProtocolSetsHubProtocolToMsgPackWithProvidedOptions()
        {
            var serviceProvider = new HubConnectionBuilder().AddMessagePackProtocol(options =>
            {
                options.SerializationContext = new SerializationContext
                {
                    SerializationMethod = SerializationMethod.Array
                };
            }).Services.BuildServiceProvider();

            var actualProtocol = Assert.IsType<MessagePackHubProtocol>(serviceProvider.GetService<IHubProtocol>());
            Assert.Equal(SerializationMethod.Array, actualProtocol.SerializationContext.SerializationMethod);
        }
    }
}
