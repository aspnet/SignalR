// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Text;
using Xunit;

namespace Microsoft.AspNetCore.Sockets.Tests
{
    public partial class MessageFormatterTests
    {
        [Fact]
        public void TextFormat_WriteMultipleMessages()
        {
            const string expectedEncoding = "0:B:;14:T:Hello,\r\nWorld!;1:C:A;12:E:Server Error;";
            var messages = new[]
            {
                CreateMessage(new byte[0]),
                CreateMessage("Hello,\r\nWorld!",MessageType.Text),
                CreateMessage("A", MessageType.Close),
                CreateMessage("Server Error", MessageType.Error)
            };

            var array = new byte[256];
            var buffer = array.Slice();
            var totalConsumed = 0;
            foreach (var message in messages)
            {
                Assert.True(MessageFormatter.TryFormatMessage(message, buffer, MessageFormat.Text, out var consumed));
                buffer = buffer.Slice(consumed);
                totalConsumed += consumed;
            }

            Assert.Equal(expectedEncoding, Encoding.UTF8.GetString(array, 0, totalConsumed));
        }

        [Theory]
        [InlineData("0:B:;", new byte[0])]
        [InlineData("8:B:q83vEg==;", new byte[] { 0xAB, 0xCD, 0xEF, 0x12 })]
        public void TextFormat_WriteBinaryMessage(string encoded, byte[] payload)
        {
            var message = CreateMessage(payload);
            var buffer = new byte[256];

            Assert.True(MessageFormatter.TryFormatMessage(message, buffer, MessageFormat.Text, out var bytesWritten));

            var encodedSpan = buffer.Slice(0, bytesWritten);
            Assert.Equal(encoded, Encoding.UTF8.GetString(encodedSpan.ToArray()));
        }

        [Theory]
        [InlineData("0:T:;", MessageType.Text, "")]
        [InlineData("3:T:ABC;", MessageType.Text, "ABC")]
        [InlineData("11:T:A\nR\rC\r\n;DEF;", MessageType.Text, "A\nR\rC\r\n;DEF")]
        [InlineData("0:C:;", MessageType.Close, "")]
        [InlineData("17:C:Connection Closed;", MessageType.Close, "Connection Closed")]
        [InlineData("0:E:;", MessageType.Error, "")]
        [InlineData("12:E:Server Error;", MessageType.Error, "Server Error")]
        public void TextFormat_WriteTextMessage(string encoded, MessageType messageType, string payload)
        {
            var message = CreateMessage(payload, messageType);
            var buffer = new byte[256];

            Assert.True(MessageFormatter.TryFormatMessage(message, buffer, MessageFormat.Text, out var bytesWritten));

            var encodedSpan = buffer.Slice(0, bytesWritten);
            Assert.Equal(encoded, Encoding.UTF8.GetString(encodedSpan.ToArray()));
        }

        [Fact]
        public void TextFormat_WriteInvalidMessages()
        {
            var message = new Message(ReadableBuffer.Create(new byte[0]).Preserve(), MessageType.Binary, endOfMessage: false);
            var ex = Assert.Throws<InvalidOperationException>(() =>
                MessageFormatter.TryFormatMessage(message, Span<byte>.Empty, MessageFormat.Text, out var written));
            Assert.Equal("Cannot format message where endOfMessage is false using this format", ex.Message);
        }

        [Theory]
        [InlineData("0:T:;", MessageType.Text, "")]
        [InlineData("3:T:ABC;", MessageType.Text, "ABC")]
        [InlineData("11:T:A\nR\rC\r\n;DEF;", MessageType.Text, "A\nR\rC\r\n;DEF")]
        [InlineData("0:C:;", MessageType.Close, "")]
        [InlineData("17:C:Connection Closed;", MessageType.Close, "Connection Closed")]
        [InlineData("0:E:;", MessageType.Error, "")]
        [InlineData("12:E:Server Error;", MessageType.Error, "Server Error")]
        public void TextFormat_ReadTextMessage(string encoded, MessageType messageType, string payload)
        {
            var buffer = Encoding.UTF8.GetBytes(encoded);

            Assert.True(MessageFormatter.TryParseMessage(buffer, MessageFormat.Text, out var message, out var consumed));
            Assert.Equal(consumed, buffer.Length);

            AssertMessage(message, messageType, payload);
        }

        [Theory]
        [InlineData("0:B:;", new byte[0])]
        [InlineData("8:B:q83vEg==;", new byte[] { 0xAB, 0xCD, 0xEF, 0x12 })]
        public void TextFormat_ReadBinaryMessage(string encoded, byte[] payload)
        {
            var buffer = Encoding.UTF8.GetBytes(encoded);

            Assert.True(MessageFormatter.TryParseMessage(buffer, MessageFormat.Text, out var message, out var consumed));
            Assert.Equal(consumed, buffer.Length);

            AssertMessage(message, MessageType.Binary, payload);
        }

        [Fact]
        public void TextFormat_ReadMultipleMessages()
        {
            const string encoded = "0:B:;14:T:Hello,\r\nWorld!;1:C:A;12:E:Server Error;";
            var buffer = (Span<byte>)Encoding.UTF8.GetBytes(encoded);

            var messages = new List<Message>();
            var consumedTotal = 0;
            while (MessageFormatter.TryParseMessage(buffer, MessageFormat.Text, out var message, out var consumed))
            {
                messages.Add(message);
                consumedTotal += consumed;
                buffer = buffer.Slice(consumed);
            }

            Assert.Equal(consumedTotal, Encoding.UTF8.GetByteCount(encoded));

            Assert.Equal(4, messages.Count);
            AssertMessage(messages[0], MessageType.Binary, new byte[0]);
            AssertMessage(messages[1], MessageType.Text, "Hello,\r\nWorld!");
            AssertMessage(messages[2], MessageType.Close, "A");
            AssertMessage(messages[3], MessageType.Error, "Server Error");
        }

        [Theory]
        [InlineData("")]
        [InlineData("ABC")]
        [InlineData("1230450945")]
        [InlineData("12ab34:")]
        [InlineData("1:asdf")]
        [InlineData("1::")]
        [InlineData("1:AB:")]
        [InlineData("5:T:A")]
        [InlineData("5:T:ABCDE")]
        [InlineData("5:T:ABCDEF")]
        [InlineData("5:X:ABCDEF")]
        [InlineData("1029348109238412903849023841290834901283409128349018239048102394:X:ABCDEF")]
        public void TextFormat_ReadInvalidMessages(string encoded)
        {
            var buffer = Encoding.UTF8.GetBytes(encoded);
            Assert.False(MessageFormatter.TryParseMessage(buffer, MessageFormat.Text, out var message, out var consumed));
            Assert.Equal(0, consumed);
        }

        private static void AssertMessage(Message message, MessageType messageType, byte[] payload)
        {
            Assert.True(message.EndOfMessage);
            Assert.Equal(messageType, message.Type);
            Assert.Equal(payload, message.Payload.Buffer.ToArray());
        }

        private static void AssertMessage(Message message, MessageType messageType, string payload)
        {
            Assert.True(message.EndOfMessage);
            Assert.Equal(messageType, message.Type);
            Assert.Equal(payload, Encoding.UTF8.GetString(message.Payload.Buffer.ToArray()));
        }

        private static Message CreateMessage(byte[] payload, MessageType type = MessageType.Binary)
        {
            return new Message(
                ReadableBuffer.Create(payload).Preserve(),
                type,
                endOfMessage: true);
        }

        private static Message CreateMessage(string payload, MessageType type)
        {
            return new Message(
                ReadableBuffer.Create(Encoding.UTF8.GetBytes(payload)).Preserve(),
                type,
                endOfMessage: true);
        }
    }
}
