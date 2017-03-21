// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO.Pipelines;
using System.Text;
using Microsoft.AspNetCore.Sockets.Internal.Formatters;
using Xunit;

namespace Microsoft.AspNetCore.Sockets.Common.Tests.Internal.Formatters
{
    public class ServerSentEventsParserTests
    {
        [Theory]
        [InlineData("data: T\r\ndata: Hello, World\r\n\r\n", "Hello, World")]
        [InlineData("data: E\r\ndata: Hello, World\r\n\r\n", "Hello, World")]
        [InlineData("data: T\r\ndata: Hello\r\ndata: , World\r\n\r\n", "Hello, World")]
        [InlineData("data: T\r\ndata: Major\r\ndata:  Key\r\ndata:  Alert\r\n\r\n", "Major Key Alert")]
        [InlineData("data: T\r\n\r\n", "")]
        public void ParseSSEMessageSuccessCases(string encodedMessage, string expectedMessage)
        {
            var buffer = Encoding.UTF8.GetBytes(encodedMessage);
            var readableBuffer = ReadableBuffer.Create(buffer);
            var parser = new ServerSentEventsMessageParser();
            var consumed = new ReadCursor();
            var examined = new ReadCursor();

            var parsePhase = parser.ParseMessage(readableBuffer, out consumed, out examined, out Message message);
            Assert.Equal(ServerSentEventsMessageParser.ParseResult.Completed, parsePhase);

            var result = Encoding.UTF8.GetString(message.Payload);
            Assert.Equal(expectedMessage, result);
        }

        [Theory]
        [InlineData("data: X\r\n", "Unknown message type: 'X'")]
        [InlineData("data: T\n", "There was an issue with the frame format")]
        [InlineData("data: X\r\n\r\n", "Unknown message type: 'X'")]
        [InlineData("data: Not the message type\r\n\r\n", "There was an error parsing the message type")]
        [InlineData("data: T\r\ndata: Hello, World\r\r\n\n", "There was an issue with the frame format")]
        [InlineData("data: Not the message type\r\r\n", "There was an error parsing the message type")]
        [InlineData("data: T\r\ndata: Hello, World\n\n", "There was an issue with the frame format")]
        [InlineData("data: T\r\nfoo: Hello, World\r\n\r\n", "Expected the message prefix 'data: '")]
        [InlineData("foo: T\r\ndata: Hello, World\r\n\r\n", "Expected the message prefix 'data: '")]
        [InlineData("food: T\r\ndata: Hello, World\r\n\r\n", "Expected the message prefix 'data: '")]
        public void ParseSSEMessageFailureCases(string encodedMessage, string expectedExceptionMessage)
        {
            var buffer = Encoding.UTF8.GetBytes(encodedMessage);
            var readableBuffer = ReadableBuffer.Create(buffer);
            var parser = new ServerSentEventsMessageParser();
            var consumed = new ReadCursor();
            var examined = new ReadCursor();

            var ex = Assert.Throws<FormatException>(() => { parser.ParseMessage(readableBuffer, out consumed, out examined, out Message message); });
            Assert.Equal(expectedExceptionMessage, ex.Message);
        }

        [Theory]
        [InlineData("")]
        [InlineData("data:")]
        [InlineData("data: T\r\nda")]
        [InlineData("data: T\r\ndata:")]
        [InlineData("data: T\r\ndata: Hello, World")]
        [InlineData("data: T\r\ndata: Hello, World\r")]
        [InlineData("data: T\r\ndata: Hello, World\r\n")]
        [InlineData("data: T\r\ndata: Hello, World\r\n\r")]
        [InlineData("data: T\r\ndata: Hello, World\r\n\r\\")]
        [InlineData("data: T\r\ndata: Major\r\ndata:  Key\rndata:  Alert\r\n\r\\")]
        [InlineData("data: T\r\ndata: Major\r\ndata:  Key\r\ndata:  Alert\r\n\r\\")]
        public void ParseSSEMessageIncompleteParseResult(string encodedMessage)
        {
            var buffer = Encoding.UTF8.GetBytes(encodedMessage);
            var readableBuffer = ReadableBuffer.Create(buffer);
            var parser = new ServerSentEventsMessageParser();
            var consumed = new ReadCursor();
            var examined = new ReadCursor();

            var parseResult = parser.ParseMessage(readableBuffer, out consumed, out examined, out Message message);

            Assert.Equal(ServerSentEventsMessageParser.ParseResult.Incomplete, parseResult);
        }
    }
}
