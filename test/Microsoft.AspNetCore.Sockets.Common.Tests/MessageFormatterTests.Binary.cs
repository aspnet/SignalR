// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using Xunit;

namespace Microsoft.AspNetCore.Sockets.Tests
{
    public partial class MessageFormatterTests
    {
        [Fact]
        public void BinaryFormat_WriteMultipleMessages()
        {
            var expectedEncoding = new byte[]
            {
                /* length: */ 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                    /* type: */ 0x01, // Binary
                    /* body: <empty> */
                /* length: */ 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x0E,
                    /* type: */ 0x00, // Text
                    /* body: */ 0x48, 0x65, 0x6C, 0x6C, 0x6F, 0x2C, 0x0D, 0x0A, 0x57, 0x6F, 0x72, 0x6C, 0x64, 0x21,
                /* length: */ 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01,
                    /* type: */ 0x03, // Close
                    /* body: */ 0x41,
                /* length: */ 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x0C,
                    /* type: */ 0x02, // Error
                    /* body: */ 0x53, 0x65, 0x72, 0x76, 0x65, 0x72, 0x20, 0x45, 0x72, 0x72, 0x6F, 0x72
            };

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
                Assert.True(MessageFormatter.TryFormatMessage(message, buffer, MessageFormat.Binary, out var consumed));
                buffer = buffer.Slice(consumed);
                totalConsumed += consumed;
            }

            Assert.Equal(expectedEncoding, array.Slice(0, totalConsumed).ToArray());
        }

        [Theory]
        [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01 }, new byte[0])]
        [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x04, 0x01, 0xAB, 0xCD, 0xEF, 0x12 }, new byte[] { 0xAB, 0xCD, 0xEF, 0x12 })]
        public void BinaryFormat_WriteBinaryMessage(byte[] encoded, byte[] payload)
        {
            var message = CreateMessage(payload);
            var buffer = new byte[256];

            Assert.True(MessageFormatter.TryFormatMessage(message, buffer, MessageFormat.Binary, out var bytesWritten));

            var encodedSpan = buffer.Slice(0, bytesWritten);
            Assert.Equal(encoded, encodedSpan.ToArray());
        }

        [Theory]
        [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }, MessageType.Text, "")]
        [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x03, 0x00, 0x41, 0x42, 0x43 }, MessageType.Text, "ABC")]
        [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x0B, 0x00, 0x41, 0x0A, 0x52, 0x0D, 0x43, 0x0D, 0x0A, 0x3B, 0x44, 0x45, 0x46 }, MessageType.Text, "A\nR\rC\r\n;DEF")]
        [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x03 }, MessageType.Close, "")]
        [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x11, 0x03, 0x43, 0x6F, 0x6E, 0x6E, 0x65, 0x63, 0x74, 0x69, 0x6F, 0x6E, 0x20, 0x43, 0x6C, 0x6F, 0x73, 0x65, 0x64 }, MessageType.Close, "Connection Closed")]
        [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x02 }, MessageType.Error, "")]
        [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x0C, 0x02, 0x53, 0x65, 0x72, 0x76, 0x65, 0x72, 0x20, 0x45, 0x72, 0x72, 0x6F, 0x72 }, MessageType.Error, "Server Error")]
        public void BinaryFormat_WriteTextMessage(byte[] encoded, MessageType messageType, string payload)
        {
            var message = CreateMessage(payload, messageType);
            var buffer = new byte[256];

            Assert.True(MessageFormatter.TryFormatMessage(message, buffer, MessageFormat.Binary, out var bytesWritten));

            var encodedSpan = buffer.Slice(0, bytesWritten);
            Assert.Equal(encoded, encodedSpan.ToArray());
        }

        [Fact]
        public void BinaryFormat_WriteInvalidMessages()
        {
            var message = new Message(ReadableBuffer.Create(new byte[0]).Preserve(), MessageType.Binary, endOfMessage: false);
            var ex = Assert.Throws<InvalidOperationException>(() =>
                MessageFormatter.TryFormatMessage(message, Span<byte>.Empty, MessageFormat.Binary, out var written));
            Assert.Equal("Cannot format message where endOfMessage is false using this format", ex.Message);
        }

        [Theory]
        [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }, MessageType.Text, "")]
        [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x03, 0x00, 0x41, 0x42, 0x43 }, MessageType.Text, "ABC")]
        [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x0B, 0x00, 0x41, 0x0A, 0x52, 0x0D, 0x43, 0x0D, 0x0A, 0x3B, 0x44, 0x45, 0x46 }, MessageType.Text, "A\nR\rC\r\n;DEF")]
        [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x03 }, MessageType.Close, "")]
        [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x11, 0x03, 0x43, 0x6F, 0x6E, 0x6E, 0x65, 0x63, 0x74, 0x69, 0x6F, 0x6E, 0x20, 0x43, 0x6C, 0x6F, 0x73, 0x65, 0x64 }, MessageType.Close, "Connection Closed")]
        [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x02 }, MessageType.Error, "")]
        [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x0C, 0x02, 0x53, 0x65, 0x72, 0x76, 0x65, 0x72, 0x20, 0x45, 0x72, 0x72, 0x6F, 0x72 }, MessageType.Error, "Server Error")]
        public void BinaryFormat_ReadTextMessage(byte[] encoded, MessageType messageType, string payload)
        {
            Assert.True(MessageFormatter.TryParseMessage(encoded, MessageFormat.Binary, out var message, out var consumed));
            Assert.Equal(consumed, encoded.Length);

            AssertMessage(message, messageType, payload);
        }

        [Theory]
        [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01 }, new byte[0])]
        [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x04, 0x01, 0xAB, 0xCD, 0xEF, 0x12 }, new byte[] { 0xAB, 0xCD, 0xEF, 0x12 })]
        public void BinaryFormat_ReadBinaryMessage(byte[] encoded, byte[] payload)
        {
            Assert.True(MessageFormatter.TryParseMessage(encoded, MessageFormat.Binary, out var message, out var consumed));
            Assert.Equal(consumed, encoded.Length);

            AssertMessage(message, MessageType.Binary, payload);
        }

        [Fact]
        public void BinaryFormat_ReadMultipleMessages()
        {
            var encoded = new byte[]
            {
                /* length: */ 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                    /* type: */ 0x01, // Binary
                    /* body: <empty> */
                /* length: */ 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x0E,
                    /* type: */ 0x00, // Text
                    /* body: */ 0x48, 0x65, 0x6C, 0x6C, 0x6F, 0x2C, 0x0D, 0x0A, 0x57, 0x6F, 0x72, 0x6C, 0x64, 0x21,
                /* length: */ 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01,
                    /* type: */ 0x03, // Close
                    /* body: */ 0x41,
                /* length: */ 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x0C,
                    /* type: */ 0x02, // Error
                    /* body: */ 0x53, 0x65, 0x72, 0x76, 0x65, 0x72, 0x20, 0x45, 0x72, 0x72, 0x6F, 0x72
            };
            var buffer = encoded.Slice();

            var messages = new List<Message>();
            var consumedTotal = 0;
            while (MessageFormatter.TryParseMessage(buffer, MessageFormat.Binary, out var message, out var consumed))
            {
                messages.Add(message);
                consumedTotal += consumed;
                buffer = buffer.Slice(consumed);
            }

            Assert.Equal(consumedTotal, encoded.Length);

            Assert.Equal(4, messages.Count);
            AssertMessage(messages[0], MessageType.Binary, new byte[0]);
            AssertMessage(messages[1], MessageType.Text, "Hello,\r\nWorld!");
            AssertMessage(messages[2], MessageType.Close, "A");
            AssertMessage(messages[3], MessageType.Error, "Server Error");
        }

        [Theory]
        [InlineData(new byte[0])] // Empty
        [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 })] // Just length
        [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x09, 0x00, 0x00, 0x00 })] // Not enough data for payload
        [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x04 })] // Invalid Type
        public void BinaryFormat_ReadInvalidMessages(byte[] encoded)
        {
            Assert.False(MessageFormatter.TryParseMessage(encoded, MessageFormat.Binary, out var message, out var consumed));
            Assert.Equal(0, consumed);
        }
    }
}
