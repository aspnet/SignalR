﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Sockets.Internal.Formatters;
using Xunit;

namespace Microsoft.AspNetCore.Sockets.Common.Tests.Internal.Formatters
{
    public class ServerSentEventsParserTests
    {
        [Theory]
        [InlineData("\r\n", "")]
        [InlineData("\r\n", "")]
        [InlineData("\r\n:\r\n", "")]
        [InlineData("\r\n:comment\r\n", "")]
        [InlineData("data: \r\r\n\r\n", "\r")]
        [InlineData(":comment\r\ndata: \r\r\n\r\n", "\r")]
        [InlineData("data: A\rB\r\n\r\n", "A\rB")]
        [InlineData("data: Hello, World\r\n\r\n", "Hello, World")]
        [InlineData("data: Hello, World\r\n\r\ndata: ", "Hello, World")]
        [InlineData("data: Hello, World\r\n\r\n:comment\r\ndata: ", "Hello, World")]
        [InlineData("data: Hello, World\r\n\r\n:comment", "Hello, World")]
        [InlineData("data: Hello, World\r\n\r\n:comment\r\n", "Hello, World")]
        [InlineData("data: Hello, World\r\n:comment\r\n\r\n", "Hello, World")]
        [InlineData("data: SGVsbG8sIFdvcmxk\r\n\r\n", "SGVsbG8sIFdvcmxk")]
        public void ParseSSEMessageSuccessCases(string encodedMessage, string expectedMessage)
        {
            var buffer = Encoding.UTF8.GetBytes(encodedMessage);
            var readableBuffer = ReadableBuffer.Create(buffer);
            var parser = new ServerSentEventsMessageParser();

            var parseResult = parser.ParseMessage(readableBuffer, out var consumed, out var examined, out var message);
            Assert.Equal(ServerSentEventsMessageParser.ParseResult.Completed, parseResult);
            Assert.Equal(consumed, examined);

            var result = Encoding.UTF8.GetString(message);
            Assert.Equal(expectedMessage, result);
        }

        [Theory]
        [InlineData("data: T\n", "Unexpected '\n' in message. A '\n' character can only be used as part of the newline sequence '\r\n'")]
        [InlineData("data: Not the message type\r\n\r\n", "Expected a data format message of the form 'data: <MesssageType>'")]
        [InlineData("data: T\r\ndata: Hello, World\r\r\n\n", "There was an error in the frame format")]
        [InlineData("data: Not the message type\r\r\n", "Expected a data format message of the form 'data: <MesssageType>'")]
        [InlineData("data: T\r\ndata: Hello, World\n\n", "Unexpected '\n' in message. A '\n' character can only be used as part of the newline sequence '\r\n'")]
        [InlineData("data: T\r\nfoo: Hello, World\r\n\r\n", "Expected the message prefix 'data: '")]
        [InlineData("foo: T\r\ndata: Hello, World\r\n\r\n", "Expected the message prefix 'data: '")]
        [InlineData("food: T\r\ndata: Hello, World\r\n\r\n", "Expected the message prefix 'data: '")]
        [InlineData("data: T\r\ndata: Hello, World\r\n\n", "There was an error in the frame format")]
        [InlineData("data: T\r\ndata: Hello\n, World\r\n\r\n", "Unexpected '\n' in message. A '\n' character can only be used as part of the newline sequence '\r\n'")]
        [InlineData("data: data: \r\n", "Expected a data format message of the form 'data: <MesssageType>'")]
        [InlineData("data: Hello, World\r\n\r\\", "Expected a \\r\\n frame ending")]
        [InlineData("data: Major\r\ndata:  Key\rndata:  Alert\r\n\r\\", "Expected a \\r\\n frame ending")]
        [InlineData("data: Major\r\ndata:  Key\r\ndata:  Alert\r\n\r\\", "Expected a \\r\\n frame ending")]
        public void ParseSSEMessageFailureCases(string encodedMessage, string expectedExceptionMessage)
        {
            var buffer = Encoding.UTF8.GetBytes(encodedMessage);
            var readableBuffer = ReadableBuffer.Create(buffer);
            var parser = new ServerSentEventsMessageParser();

            var ex = Assert.Throws<FormatException>(() => { parser.ParseMessage(readableBuffer, out var consumed, out var examined, out var message); });
            Assert.Equal(expectedExceptionMessage, ex.Message);
        }

        [Theory]
        [InlineData("")]
        [InlineData(":")]
        [InlineData(":comment")]
        [InlineData(":comment\r\n")]
        [InlineData("data:")]
        [InlineData("data: \r")]
        [InlineData("data: T\r\nda")]
        [InlineData("data: T\r\ndata:")]
        [InlineData("data: T\r\ndata: Hello, World")]
        [InlineData("data: T\r\ndata: Hello, World\r")]
        [InlineData("data: T\r\ndata: Hello, World\r\n")]
        [InlineData("data: T\r\ndata: Hello, World\r\n\r")]
        [InlineData("data: B\r\ndata: SGVsbG8sIFd")]
        [InlineData(":\r\ndata:")]
        [InlineData("data: T\r\n:\r\n")]
        [InlineData("data: T\r\n:\r\ndata:")]
        [InlineData("data: T\r\ndata: Hello, World\r\n:comment")]
        public void ParseSSEMessageIncompleteParseResult(string encodedMessage)
        {
            var buffer = Encoding.UTF8.GetBytes(encodedMessage);
            var readableBuffer = ReadableBuffer.Create(buffer);
            var parser = new ServerSentEventsMessageParser();

            var parseResult = parser.ParseMessage(readableBuffer, out var consumed, out var examined, out var message);

            Assert.Equal(ServerSentEventsMessageParser.ParseResult.Incomplete, parseResult);
        }

        [Theory]
        [InlineData(new[] { "d", "ata: T\r\ndata: Hello, World\r\n\r\n" }, "Hello, World")]
        [InlineData(new[] { "data: T", "\r\ndata: Hello, World\r\n\r\n" }, "Hello, World")]
        [InlineData(new[] { "data: T\r", "\ndata: Hello, World\r\n\r\n" }, "Hello, World")]
        [InlineData(new[] { "data: T\r\n", "data: Hello, World\r\n\r\n" }, "Hello, World")]
        [InlineData(new[] { "data: T\r\nd", "ata: Hello, World\r\n\r\n" }, "Hello, World")]
        [InlineData(new[] { "data: T\r\ndata: ", "Hello, World\r\n\r\n" }, "Hello, World")]
        [InlineData(new[] { "data: T\r\ndata: Hello, World", "\r\n\r\n" }, "Hello, World")]
        [InlineData(new[] { "data: T\r\ndata: Hello, World\r\n", "\r\n" }, "Hello, World")]
        [InlineData(new[] { "data: ", "T\r\ndata: Hello, World\r\n\r\n" }, "Hello, World")]
        [InlineData(new[] { ":", "comment", "\r\n", "d", "ata: T\r\ndata: Hello, World\r\n\r\n" }, "Hello, World")]
        [InlineData(new[] { "data: T\r\n", ":comment", "\r\n", "data: Hello, World", "\r\n\r\n" }, "Hello, World")]
        [InlineData(new[] { "data: T\r\ndata: Hello, World\r\n", ":comment\r\n", "\r\n" }, "Hello, World")]
        [InlineData(new[] { "data: B\r\ndata: SGVs", "bG8sIFdvcmxk\r\n\r\n" }, "Hello, World")]
        public async Task ParseMessageAcrossMultipleReadsSuccess(string[] messageParts, string expectedMessage)
        {
            using (var pipeFactory = new PipeFactory())
            {
                var parser = new ServerSentEventsMessageParser();
                var pipe = pipeFactory.Create();

                byte[] message = null;
                ReadCursor consumed = default(ReadCursor), examined = default(ReadCursor);

                for (var i = 0; i < messageParts.Length; i++)
                {
                    var messagePart = messageParts[i];
                    await pipe.Writer.WriteAsync(Encoding.UTF8.GetBytes(messagePart));
                    var result = await pipe.Reader.ReadAsync();

                    var parseResult = parser.ParseMessage(result.Buffer, out consumed, out examined, out message);
                    pipe.Reader.Advance(consumed, examined);

                    // parse result should be complete only after we parsed the last message part
                    var expectedResult =
                        i == messageParts.Length - 1
                            ? ServerSentEventsMessageParser.ParseResult.Completed
                            : ServerSentEventsMessageParser.ParseResult.Incomplete;

                    Assert.Equal(expectedResult, parseResult);
                }

                Assert.Equal(consumed, examined);

                var resultMessage = Encoding.UTF8.GetString(message);
                Assert.Equal(expectedMessage, resultMessage);
            }
        }

        [Theory]
        [InlineData("data: T", "\n", "Unexpected '\n' in message. A '\n' character can only be used as part of the newline sequence '\r\n'")]
        [InlineData("data: ", "Not the message type\r\n\r\n", "Expected a data format message of the form 'data: <MesssageType>'")]
        [InlineData("data: T\r\n", "data: Hello, World\r\r\n\n", "There was an error in the frame format")]
        [InlineData("data:", " Not the message type\r\r\n", "Expected a data format message of the form 'data: <MesssageType>'")]
        [InlineData("data: T\r\n", "data: Hello, World\n\n", "Unexpected '\n' in message. A '\n' character can only be used as part of the newline sequence '\r\n'")]
        [InlineData("data: T\r\nf", "oo: Hello, World\r\n\r\n", "Expected the message prefix 'data: '")]
        [InlineData("foo", ": T\r\ndata: Hello, World\r\n\r\n", "Expected the message prefix 'data: '")]
        [InlineData("food:", " T\r\ndata: Hello, World\r\n\r\n", "Expected the message prefix 'data: '")]
        [InlineData("data: T\r\ndata: Hello, W", "orld\r\n\n", "There was an error in the frame format")]
        [InlineData("data: T\r\nda", "ta: Hello\n, World\r\n\r\n", "Unexpected '\n' in message. A '\n' character can only be used as part of the newline sequence '\r\n'")]
        [InlineData("data:", " data: \r\n", "Expected a data format message of the form 'data: <MesssageType>'")]
        [InlineData("data: ", "T\r\ndata: Major\r\ndata:  Key\r\ndata:  Alert\r\n\r\\", "Expected a \\r\\n frame ending")]
        [InlineData("data: ", "This is not a message type\r\n", "Expected a data format message of the form 'data: <MesssageType>'")]
        [InlineData("data: B\r\ndata: SGVs", "bG8sIFdvcmxk\r\n\n\n", "There was an error in the frame format")]
        [InlineData("data: T", "his is not a message type\r\n", "Expected a data format message of the form 'data: <MesssageType>'")]
        public async Task ParseMessageAcrossMultipleReadsFailure(string encodedMessagePart1, string encodedMessagePart2, string expectedMessage)
        {
            using (var pipeFactory = new PipeFactory())
            {
                var pipe = pipeFactory.Create();

                // Read the first part of the message
                await pipe.Writer.WriteAsync(Encoding.UTF8.GetBytes(encodedMessagePart1));

                var result = await pipe.Reader.ReadAsync();
                var parser = new ServerSentEventsMessageParser();

                var parseResult = parser.ParseMessage(result.Buffer, out var consumed, out var examined, out var buffer);
                Assert.Equal(ServerSentEventsMessageParser.ParseResult.Incomplete, parseResult);

                pipe.Reader.Advance(consumed, examined);

                // Send the rest of the data and parse the complete message
                await pipe.Writer.WriteAsync(Encoding.UTF8.GetBytes(encodedMessagePart2));
                result = await pipe.Reader.ReadAsync();

                var ex = Assert.Throws<FormatException>(() => parser.ParseMessage(result.Buffer, out consumed, out examined, out buffer));
                Assert.Equal(expectedMessage, ex.Message);

            }
        }

        [Theory]
        [InlineData("data: foo\r\n\r\n", "data: bar\r\n\r\n")]
        [InlineData("data: Zm9v\r\n\r\n", "data: YmFy\r\n\r\n")]
        public async Task ParseMultipleMessagesText(string message1, string message2)
        {
            using (var pipeFactory = new PipeFactory())
            {
                var pipe = pipeFactory.Create();

                // Read the first part of the message
                await pipe.Writer.WriteAsync(Encoding.UTF8.GetBytes(message1 + message2));

                var result = await pipe.Reader.ReadAsync();
                var parser = new ServerSentEventsMessageParser();

                var parseResult = parser.ParseMessage(result.Buffer, out var consumed, out var examined, out var message);
                Assert.Equal(ServerSentEventsMessageParser.ParseResult.Completed, parseResult);
                Assert.Equal("foo", Encoding.UTF8.GetString(message));
                Assert.Equal(consumed, result.Buffer.Move(result.Buffer.Start, message1.Length));
                pipe.Reader.Advance(consumed, examined);
                Assert.Equal(consumed, examined);

                parser.Reset();

                result = await pipe.Reader.ReadAsync();
                parseResult = parser.ParseMessage(result.Buffer, out consumed, out examined, out message);
                Assert.Equal(ServerSentEventsMessageParser.ParseResult.Completed, parseResult);
                Assert.Equal("bar", Encoding.UTF8.GetString(message));
                pipe.Reader.Advance(consumed, examined);
            }
        }

        public static IEnumerable<object[]> MultilineMessages
        {
            get
            {
                yield return new object[] { "data: Shaolin\r\ndata:  Fantastic\r\n\r\n", "Shaolin" + Environment.NewLine + " Fantastic", MessageType.Text };
                yield return new object[] { "data: The\r\ndata: Get\r\ndata: Down\r\n\r\n", "The" + Environment.NewLine + "Get" + Environment.NewLine + "Down", MessageType.Text };
            }
        }

        [Theory]
        [MemberData(nameof(MultilineMessages))]
        public void ParseMessagesWithMultipleDataLines(string encodedMessage, string expectedMessage, MessageType expectedMessageType)
        {
            var buffer = Encoding.UTF8.GetBytes(encodedMessage);
            var readableBuffer = ReadableBuffer.Create(buffer);
            var parser = new ServerSentEventsMessageParser();

            var parseResult = parser.ParseMessage(readableBuffer, out var consumed, out var examined, out var message);
            Assert.Equal(ServerSentEventsMessageParser.ParseResult.Completed, parseResult);
            Assert.Equal(consumed, examined);

            var result = Encoding.UTF8.GetString(message);
            Assert.Equal(expectedMessage, result);
        }
    }
}
