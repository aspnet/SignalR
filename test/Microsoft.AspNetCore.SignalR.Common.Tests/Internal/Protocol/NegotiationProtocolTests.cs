// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Text;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Xunit;

namespace Microsoft.AspNetCore.SignalR.Common.Tests.Internal.Protocol
{
    public class NegotiationProtocolTests
    {
        [Fact]
        public void CanRoundtripNegotiationRequest()
        {
            var negotiationMessage = new NegotiationRequestMessage(protocol: "dummy");
            using (var ms = new MemoryStream())
            {
                NegotiationProtocol.WriteRequestMessage(negotiationMessage, ms);
                Assert.True(NegotiationProtocol.TryParseRequestMessage(ms.ToArray(), out var deserializedMessage));

                Assert.NotNull(deserializedMessage);
                Assert.Equal(negotiationMessage.Protocol, deserializedMessage.Protocol);
            }
        }

        [Fact]
        public void CanRoundtripNegotiationResponse()
        {
            var negotiationMessage = new NegotiationResponseMessage(error: "dummy");
            using (var ms = new MemoryStream())
            {
                NegotiationProtocol.WriteResponseMessage(negotiationMessage, ms);
                Assert.True(NegotiationProtocol.TryParseResponseMessage(ms.ToArray(), out var deserializedMessage));

                Assert.NotNull(deserializedMessage);
                Assert.Equal(negotiationMessage.Error, deserializedMessage.Error);
            }
        }

        [Theory]
        [InlineData("", "Unable to parse payload as a negotiation message.")]
        [InlineData("42\u001e", "Unexpected JSON Token Type 'Integer'. Expected a JSON Object.")]
        [InlineData("\"42\"\u001e", "Unexpected JSON Token Type 'String'. Expected a JSON Object.")]
        [InlineData("null\u001e", "Unexpected JSON Token Type 'Null'. Expected a JSON Object.")]
        [InlineData("{}\u001e", "Missing required property 'protocol'.")]
        [InlineData("[]\u001e", "Unexpected JSON Token Type 'Array'. Expected a JSON Object.")]
        public void ParsingNegotiationRequestMessageThrowsForInvalidMessages(string payload, string expectedMessage)
        {
            var message = Encoding.UTF8.GetBytes(payload);

            var exception = Assert.Throws<InvalidDataException>(() =>
                Assert.True(NegotiationProtocol.TryParseRequestMessage(message, out var deserializedMessage)));

            Assert.Equal(expectedMessage, exception.Message);
        }

        [Theory]
        [InlineData("", "Unable to parse payload as a negotiation message.")]
        [InlineData("42\u001e", "Unexpected JSON Token Type 'Integer'. Expected a JSON Object.")]
        [InlineData("\"42\"\u001e", "Unexpected JSON Token Type 'String'. Expected a JSON Object.")]
        [InlineData("null\u001e", "Unexpected JSON Token Type 'Null'. Expected a JSON Object.")]
        [InlineData("[]\u001e", "Unexpected JSON Token Type 'Array'. Expected a JSON Object.")]
        public void ParsingNegotiationResponseMessageThrowsForInvalidMessages(string payload, string expectedMessage)
        {
            var message = Encoding.UTF8.GetBytes(payload);

            var exception = Assert.Throws<InvalidDataException>(() =>
                Assert.True(NegotiationProtocol.TryParseResponseMessage(message, out var deserializedMessage)));

            Assert.Equal(expectedMessage, exception.Message);
        }
    }
}
