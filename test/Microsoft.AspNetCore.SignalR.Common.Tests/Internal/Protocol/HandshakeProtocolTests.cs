// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Text;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Xunit;

namespace Microsoft.AspNetCore.SignalR.Common.Tests.Internal.Protocol
{
    public class HandshakeProtocolTests
    {
        [Fact]
        public void CanRoundtripHandshakeRequest()
        {
            var requestMessage = new HandshakeRequestMessage(protocol: "dummy");
            using (var ms = new MemoryStream())
            {
                HandshakeProtocol.WriteRequestMessage(requestMessage, ms);
                Assert.True(HandshakeProtocol.TryParseRequestMessage(ms.ToArray(), out var deserializedMessage));

                Assert.NotNull(deserializedMessage);
                Assert.Equal(requestMessage.Protocol, deserializedMessage.Protocol);
            }
        }

        [Fact]
        public void CanRoundtripHandshakeResponse()
        {
            var responseMessage = new HandshakeResponseMessage(error: "dummy");
            using (var ms = new MemoryStream())
            {
                HandshakeProtocol.WriteResponseMessage(responseMessage, ms);
                Assert.True(HandshakeProtocol.TryParseResponseMessage(ms.ToArray(), out var deserializedMessage));

                Assert.NotNull(deserializedMessage);
                Assert.Equal(responseMessage.Error, deserializedMessage.Error);
            }
        }

        [Theory]
        [InlineData("", "Unable to parse payload as a handshake message.")]
        [InlineData("42\u001e", "Unexpected JSON Token Type 'Integer'. Expected a JSON Object.")]
        [InlineData("\"42\"\u001e", "Unexpected JSON Token Type 'String'. Expected a JSON Object.")]
        [InlineData("null\u001e", "Unexpected JSON Token Type 'Null'. Expected a JSON Object.")]
        [InlineData("{}\u001e", "Missing required property 'protocol'.")]
        [InlineData("[]\u001e", "Unexpected JSON Token Type 'Array'. Expected a JSON Object.")]
        public void ParsingHandshakeRequestMessageThrowsForInvalidMessages(string payload, string expectedMessage)
        {
            var message = Encoding.UTF8.GetBytes(payload);

            var exception = Assert.Throws<InvalidDataException>(() =>
                Assert.True(HandshakeProtocol.TryParseRequestMessage(message, out var deserializedMessage)));

            Assert.Equal(expectedMessage, exception.Message);
        }

        [Theory]
        [InlineData("", "Unable to parse payload as a handshake message.")]
        [InlineData("42\u001e", "Unexpected JSON Token Type 'Integer'. Expected a JSON Object.")]
        [InlineData("\"42\"\u001e", "Unexpected JSON Token Type 'String'. Expected a JSON Object.")]
        [InlineData("null\u001e", "Unexpected JSON Token Type 'Null'. Expected a JSON Object.")]
        [InlineData("[]\u001e", "Unexpected JSON Token Type 'Array'. Expected a JSON Object.")]
        public void ParsingHandshakeResponseMessageThrowsForInvalidMessages(string payload, string expectedMessage)
        {
            var message = Encoding.UTF8.GetBytes(payload);

            var exception = Assert.Throws<InvalidDataException>(() =>
                Assert.True(HandshakeProtocol.TryParseResponseMessage(message, out var deserializedMessage)));

            Assert.Equal(expectedMessage, exception.Message);
        }
    }
}
