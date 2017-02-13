using System;
using System.IO.Pipelines;
using System.Text;
using Xunit;

namespace Microsoft.AspNetCore.Sockets.Tests
{
    public class MessageFormatterTests
    {
        [Theory]
        [InlineData("0:B:;", new byte[0])]
        [InlineData("8:B:q83vEg==;", new byte[] { 0xAB, 0xCD, 0xEF, 0x12 })]
        public void WriteBinaryMessage(string encoded, byte[] payload)
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
        public void WriteTextMessage(string encoded, MessageType messageType, string payload)
        {
            var message = CreateMessage(payload, messageType);
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
        public void ReadTextMessage(string encoded, MessageType messageType, string payload)
        {
            var buffer = Encoding.UTF8.GetBytes(encoded);

            Assert.True(MessageFormatter.TryParseMessage(buffer, MessageFormat.Text, out var message, out var consumed));
            Assert.Equal(consumed, buffer.Length);

            AssertMessage(message, messageType, payload);
        }

        [Theory]
        [InlineData("0:B:;", new byte[0])]
        [InlineData("8:B:q83vEg==;", new byte[] { 0xAB, 0xCD, 0xEF, 0x12 })]
        public void ReadBinaryMessage(string encoded, byte[] payload)
        {
            var buffer = Encoding.UTF8.GetBytes(encoded);

            Assert.True(MessageFormatter.TryParseMessage(buffer, MessageFormat.Text, out var message, out var consumed));
            Assert.Equal(consumed, buffer.Length);

            AssertMessage(message, MessageType.Binary, payload);
        }

        [Fact]
        public void WriteInvalidMessages()
        {
            var message = new Message(ReadableBuffer.Create(new byte[0]).Preserve(), MessageType.Binary, endOfMessage: false);
            var ex = Assert.Throws<InvalidOperationException>(() =>
                MessageFormatter.TryFormatMessage(message, Span<byte>.Empty, MessageFormat.Text, out var written));
            Assert.Equal("Cannot format message where endOfMessage is false using this format", ex.Message);
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
        public void ReadInvalidMessages(string encoded)
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

        private static Message CreateMessage(string payload, MessageType type = MessageType.Text)
        {
            return new Message(
                ReadableBuffer.Create(Encoding.UTF8.GetBytes(payload)).Preserve(),
                type,
                endOfMessage: true);
        }
    }
}
