// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO.Pipelines;
using System.Text;
using Microsoft.AspNetCore.Sockets.Internal.Formatters;
using Xunit;
using System.IO.Pipelines.Testing;
using System.IO;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.Sockets.Common.Tests.Internal.Formatters
{
    public class ServerSentEventsParserTests
    {
        //[Theory]
        //[InlineData("data: T\r\ndata: Hello, World\r\n\r\n", "Hello, World")]
        //[InlineData("data: E\r\ndata: Hello, World\r\n\r\n", "Hello, World")]
        //[InlineData("data: T\r\ndata: Hello\r\ndata: , World\r\n\r\n", "Hello, World")]
        //[InlineData("data: T\r\ndata: Major\r\ndata:  Key\r\ndata:  Alert\r\n\r\n", "Major Key Alert")]
        //[InlineData("data: T\r\n\r\n", "")]
        //[InlineData("data: T\r\ndata: Hello, World\r\n\r\ndata: ", "Hello, World")]
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

        //[Theory]
        //[InlineData("data: X\r\n", "Unknown message type: 'X'")]
        //[InlineData("data: T\n", "A '\\n' character can only be used as a line ending")]
        //[InlineData("data: X\r\n\r\n", "Unknown message type: 'X'")]
        //[InlineData("data: Not the message type\r\n\r\n", "Unknown message type: 'N'")]
        //[InlineData("data: T\r\ndata: Hello, World\r\r\n\n", "There was an error in the frame format")]
        //[InlineData("data: Not the message type\r\r\n", "Unknown message type: 'N'")]
        //[InlineData("data: T\r\ndata: Hello, World\n\n", "A '\\n' character can only be used as a line ending")]
        //[InlineData("data: T\r\nfoo: Hello, World\r\n\r\n", "Expected the message prefix 'data: '")]
        //[InlineData("foo: T\r\ndata: Hello, World\r\n\r\n", "Expected the message prefix 'data: '")]
        //[InlineData("food: T\r\ndata: Hello, World\r\n\r\n", "Expected the message prefix 'data: '")]
        //[InlineData("data: T\r\ndata: Hello, World\r\n\n", "There was an error in the frame format")]
        //[InlineData("data: T\r\ndata: Hello\n, World\r\n\r\n", "A '\\n' character can only be used as a line ending")]
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

        //[Theory]
        //[InlineData("")]
        //[InlineData("data:")]
        //[InlineData("data: \r")]
        //[InlineData("data: T\r\nda")]
        //[InlineData("data: T\r\ndata:")]
        //[InlineData("data: T\r\ndata: Hello, World")]
        //[InlineData("data: T\r\ndata: Hello, World\r")]
        //[InlineData("data: T\r\ndata: Hello, World\r\n")]
        //[InlineData("data: T\r\ndata: Hello, World\r\n\r")]
        //[InlineData("data: T\r\ndata: Hello, World\r\n\r\\")]
        //[InlineData("data: T\r\ndata: Major\r\ndata:  Key\rndata:  Alert\r\n\r\\")]
        //[InlineData("data: T\r\ndata: Major\r\ndata:  Key\r\ndata:  Alert\r\n\r\\")]
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

        [Theory]
        //[InlineData("d", "ata: T\r\ndata: Hello, World\r\n\r\n", "Hello, World")]
        //[InlineData("data: T\\", "r\ndata: Hello, World\r\n\r\n", "Hello, World")]
        //[InlineData("data: T\r", "\ndata: Hello, World\r\n\r\n", "Hello, World")]
        //[InlineData("data: T\r\n", "data: Hello, World\r\n\r\n", "Hello, World")]
        [InlineData("data: T\r\nd", "ata: Hello, World\r\n\r\n", "Hello, World")]
        //[InlineData("data: T\r\ndata: ", "Hello, World\r\n\r\n", "Hello, World")]
        //[InlineData("data: T\r\ndata: Hello, World\\", "r\n\r\n", "Hello, World")]
        //[InlineData("data: T\r\ndata: Hello, World\r\n", "\r\n", "Hello, World")]
        //[InlineData("data: T", "\r\ndata: Hello, World\r\n\r\n", "Hello, World")]
        //[InlineData("data: ", "T\r\ndata: Hello, World\r\n\r\n", "Hello, World")]
        public async Task ParseMessageAcrossMultipleBuffers(string encodedMessagePart1, string encodedMessagePart2, string expectedMessage)
        {
            var stream = new MemoryStream();

            var streamWriter = new StreamWriter(stream);
            streamWriter.Write(encodedMessagePart1);
            streamWriter.Flush();
            stream.Seek(0, SeekOrigin.Begin);

            var pipeFactory = new PipeFactory();
            var writer = pipeFactory.CreateWriter(stream);
            var reader = pipeFactory.CreateReader(stream);
            await writer.WriteAsync(Encoding.UTF8.GetBytes(encodedMessagePart1));
            stream.Seek(0, SeekOrigin.Begin);
            stream.Flush(); 

            var result = await reader.ReadAsync();
            // Read the first part of the message
            //var pipelineReader = stream.AsPipelineReader();
            //var result = await pipelineReader.ReadAsync();

            var consumed = result.Buffer.Start;
            var examined = result.Buffer.Start;

            var parser = new ServerSentEventsMessageParser();

            var parseResult = parser.ParseMessage(result.Buffer, out consumed, out examined, out Message message);
            Assert.Equal(ServerSentEventsMessageParser.ParseResult.Incomplete, parseResult);

            reader.Advance(consumed, examined);


            // Send the rest of the data and parse the complete message

            //streamWriter.Write(encodedMessagePart2);
            //streamWriter.Flush();
            //stream.Seek(encodedMessagePart1.Length, SeekOrigin.Begin);

            await writer.WriteAsync(Encoding.UTF8.GetBytes(encodedMessagePart2));
            stream.Seek(0, SeekOrigin.Begin);
            stream.Flush();

            result = await reader.ReadAsync();

            parseResult = parser.ParseMessage(result.Buffer, out consumed, out examined, out  message);
            Assert.Equal(ServerSentEventsMessageParser.ParseResult.Completed, parseResult);

            var resultMessage = Encoding.UTF8.GetString(message.Payload);
            Assert.Equal(expectedMessage, resultMessage);
        }
    }
}
