using System;
using System.IO.Pipelines;
using System.Text;
using Xunit;

namespace Microsoft.AspNetCore.Sockets.Tests
{
    public class MessageBatchFormatterTests
    {
        [Theory]
        [InlineData("T0:T:;;", MessageType.Text, "")]
        [InlineData("T3:T:ABC;;", MessageType.Text, "ABC")]
        [InlineData("T11:T:A\nR\rC\r\n;DEF;;", MessageType.Text, "A\nR\rC\r\n;DEF")]
        [InlineData("T0:C:;;", MessageType.Close, "")]
        [InlineData("T17:C:Connection Closed;;", MessageType.Close, "Connection Closed")]
        [InlineData("T0:E:;;", MessageType.Error, "")]
        [InlineData("T12:E:Server Error;;", MessageType.Error, "Server Error")]
        public void WriteSingleTextMessage(string encoded, MessageType messageType, string payload)
        {
            var messages = new[] {
                new Message(
                    ReadableBuffer.Create(Encoding.UTF8.GetBytes(payload)).Preserve(),
                    messageType,
                    endOfMessage: true)
            };
            var buffer = new byte[256];

            Assert.True(MessageBatchFormatter.TryFormatMessages(messages, buffer, MessageFormat.Text, out int length));

            var encodedSpan = buffer.Slice(0, length);
            Assert.Equal(encoded, Encoding.UTF8.GetString(encodedSpan.ToArray()));
        }

        [Theory]
        [InlineData("T0:T:;;", MessageType.Text, "")]
        [InlineData("T3:T:ABC;;", MessageType.Text, "ABC")]
        [InlineData("T11:T:A\nR\rC\r\n;DEF;;", MessageType.Text, "A\nR\rC\r\n;DEF")]
        [InlineData("T0:C:;;", MessageType.Close, "")]
        [InlineData("T17:C:Connection Closed;;", MessageType.Close, "Connection Closed")]
        [InlineData("T0:E:;;", MessageType.Error, "")]
        [InlineData("T12:E:Server Error;;", MessageType.Error, "Server Error")]
        public void ReadSingleTextMessage(string encoded, MessageType messageType, string payload)
        {
            var buffer = Encoding.UTF8.GetBytes(encoded);

            Assert.True(MessageBatchFormatter.TryParseMessages(buffer, out var messages, out var consumed));
            Assert.Equal(consumed, buffer.Length);

            Assert.Equal(1, messages.Count);
            AssertMessage(messages[0], messageType, payload);
        }

        [Theory]
        [InlineData("T0:B:;;", new byte[0])]
        [InlineData("T8:B:q83vEg==;;", new byte[] { 0xAB, 0xCD, 0xEF, 0x12 })]
        public void ReadSingleBinaryMessage(string encoded, byte[] payload)
        {
            var buffer = Encoding.UTF8.GetBytes(encoded);

            Assert.True(MessageBatchFormatter.TryParseMessages(buffer, out var messages, out var consumed));
            Assert.Equal(consumed, buffer.Length);

            Assert.Equal(1, messages.Count);
            AssertMessage(messages[0], MessageType.Binary, payload);
        }

        [Fact]
        public void ReadMultipleMessages()
        {
            const string encoded = "T0:B:;14:T:Hello,\r\nWorld!;1:C:A;12:E:Server Error;;";
            var buffer = Encoding.UTF8.GetBytes(encoded);

            Assert.True(MessageBatchFormatter.TryParseMessages(buffer, out var messages, out var consumed));
            Assert.Equal(consumed, buffer.Length);

            Assert.Equal(4, messages.Count);
            AssertMessage(messages[0], MessageType.Binary, new byte[0]);
            AssertMessage(messages[1], MessageType.Text, "Hello,\r\nWorld!");
            AssertMessage(messages[2], MessageType.Close, "A");
            AssertMessage(messages[3], MessageType.Error, "Server Error");
        }

        [Theory]
        [InlineData("")]
        [InlineData("ABC")]
        [InlineData("T1230450945")]
        [InlineData("T12ab34:")]
        [InlineData("T1:asdf")]
        [InlineData("T1::")]
        [InlineData("T1:AB:")]
        [InlineData("T5:T:A")]
        [InlineData("T5:T:ABCDE")]
        [InlineData("T5:T:ABCDEF")]
        [InlineData("T5:X:ABCDEF")]
        [InlineData("T1029348109238412903849023841290834901283409128349018239048102394:X:ABCDEF")]
        public void InvalidMessages(string encoded)
        {
            var buffer = Encoding.UTF8.GetBytes(encoded);
            Assert.False(MessageBatchFormatter.TryParseMessages(buffer, out var messages, out var consumed));
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
