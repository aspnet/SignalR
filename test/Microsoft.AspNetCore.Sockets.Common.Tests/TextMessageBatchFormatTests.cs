using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Linq;
using System.Text;
using Xunit;

namespace Microsoft.AspNetCore.Sockets.Tests
{
    public class TextMessageBatchFormatTests
    {
        public static IEnumerable<object[]> MeasureMessagesData
        {
            get
            {
                yield return new object[] { new[] { CreateMessage("abc") }, 9 };
                yield return new object[] { new[] { CreateMessage("abc"), CreateMessage("cde", MessageType.Close) }, 17 };
                yield return new object[] { new[] { CreateMessage(new byte[] { 0x0A, 0x0B }) }, 10 };
                yield return new object[] { new[] { CreateMessage(new byte[] { 0x0A, 0x0B, 0x0C }) }, 10 }; // Same length, because of the magic of Base-64
            }
        }

        [Theory]
        [MemberData(nameof(MeasureMessagesData))]
        public void MeasureMessagesTests(Message[] messages, int length)
        {
            Assert.Equal(length, TextMessageBatchFormat.MeasureMessages(messages));
        }

        [Theory]
        [InlineData("T0:T:;", MessageType.Text, "")]
        [InlineData("T3:T:ABC;", MessageType.Text, "ABC")]
        [InlineData("T11:T:A\nR\rC\r\n;DEF;", MessageType.Text, "A\nR\rC\r\n;DEF")]
        [InlineData("T0:C:;", MessageType.Close, "")]
        [InlineData("T17:C:Connection Closed;", MessageType.Close, "Connection Closed")]
        [InlineData("T0:E:;", MessageType.Error, "")]
        [InlineData("T12:E:Server Error;", MessageType.Error, "Server Error")]
        public void WriteSingleTextMessage(string encoded, MessageType messageType, string payload)
        {
            var messages = new[] {
                new Message(
                    ReadableBuffer.Create(Encoding.UTF8.GetBytes(payload)).Preserve(),
                    messageType,
                    endOfMessage: true)
            };
            var encodedLength = TextMessageBatchFormat.MeasureMessages(messages);
            var buffer = new byte[encodedLength];

            Assert.True(TextMessageBatchFormat.WriteMessages(buffer, messages));

            Assert.Equal(encoded, Encoding.UTF8.GetString(buffer));
        }

        [Theory]
        [InlineData("T0:T:;", MessageType.Text, "")]
        [InlineData("T3:T:ABC;", MessageType.Text, "ABC")]
        [InlineData("T11:T:A\nR\rC\r\n;DEF;", MessageType.Text, "A\nR\rC\r\n;DEF")]
        [InlineData("T0:C:;", MessageType.Close, "")]
        [InlineData("T17:C:Connection Closed;", MessageType.Close, "Connection Closed")]
        [InlineData("T0:E:;", MessageType.Error, "")]
        [InlineData("T12:E:Server Error;", MessageType.Error, "Server Error")]
        public void ReadSingleTextMessage(string encoded, MessageType messageType, string payload)
        {
            var buffer = Encoding.UTF8.GetBytes(encoded);
            var messages = TextMessageBatchFormat.ReadMessages(buffer).ToArray();

            Assert.Equal(1, messages.Length);
            AssertMessage(messages[0], messageType, payload);
        }

        [Theory]
        [InlineData("T0:B:;", new byte[0])]
        [InlineData("T8:B:q83vEg==;", new byte[] { 0xAB, 0xCD, 0xEF, 0x12 })]
        public void ReadSingleBinaryMessage(string encoded, byte[] payload)
        {
            var buffer = Encoding.UTF8.GetBytes(encoded);
            var messages = TextMessageBatchFormat.ReadMessages(buffer).ToArray();

            Assert.Equal(1, messages.Length);
            AssertMessage(messages[0], MessageType.Binary, payload);
        }

        [Fact]
        public void ReadMultipleMessages()
        {
            const string encoded = "T0:B:;14:T:Hello,\r\nWorld!;1:C:A;12:E:Server Error;";
            var buffer = Encoding.UTF8.GetBytes(encoded);
            var messages = TextMessageBatchFormat.ReadMessages(buffer).ToArray();

            Assert.Equal(4, messages.Length);
            AssertMessage(messages[0], MessageType.Binary, new byte[0]);
            AssertMessage(messages[1], MessageType.Text, "Hello,\r\nWorld!");
            AssertMessage(messages[2], MessageType.Close, "A");
            AssertMessage(messages[3], MessageType.Error, "Server Error");
        }

        [Theory]
        [InlineData("", "Missing 'T' prefix in Text Message Batch.")]
        [InlineData("ABC", "Missing 'T' prefix in Text Message Batch.")]
        [InlineData("T1230450945", "Unexpected end-of-message while reading Length field.")]
        [InlineData("T12ab34:", "Invalid length.")]
        [InlineData("T1:asdf", "Unexpected end-of-message while reading Type field.")]
        [InlineData("T1::", "Type field must be exactly one byte long.")]
        [InlineData("T1:AB:", "Type field must be exactly one byte long.")]
        [InlineData("T5:T:A", "Unexpected end-of-message while reading Payload field.")]
        [InlineData("T5:T:ABCDE", "Unexpected end-of-message while reading Payload field.")]
        [InlineData("T5:T:ABCDEF", "Payload is missing trailer character ';'.")]
        public void InvalidMessages(string encoded, string message)
        {
            var ex = Assert.Throws<FormatException>(() => TextMessageBatchFormat.ReadMessages(Encoding.UTF8.GetBytes(encoded)).ToArray());
            Assert.Equal(message, ex.Message);
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
