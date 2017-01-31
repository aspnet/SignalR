using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Text;

namespace Microsoft.AspNetCore.Sockets
{
    public static class TextMessageBatchFormat
    {
        /// <summary>
        /// Decodes the messages encoded in the provided data.
        /// </summary>
        /// <param name="data">The data containing the encoded messages to read</param>
        /// <exception cref="FormatException">The input does not adhere to the protocol</exception>
        /// <returns>A list of <see cref="Message"/> values decoded from the buffer</returns>
        public static IEnumerable<Message> ReadMessages(ReadOnlySpan<byte> data)
        {
            if (data.Length < 1 || data[0] != 'T')
            {
                throw new FormatException("Missing 'T' prefix in Text Message Batch.");
            }

            int cursor = 1;
            while (cursor < data.Length)
            {
                // Parse the length
                int length = ParseLength(data, ref cursor);
                cursor++;

                // Parse the type
                var type = ParseType(data, ref cursor);
                cursor++;

                // Read the payload
                var payload = ParsePayload(data, length, type, ref cursor);
                cursor++;

                yield return new Message(payload, type, endOfMessage: true);
            }
        }

        /// <summary>
        /// Attempts to write the specified messages to the target buffer
        /// </summary>
        /// <param name="target">The buffer in which to encode the messages</param>
        /// <param name="messages">The messages to encode</param>
        /// <returns>A boolean indicating if there was enough space in the buffer to encode the message.</returns>
        public static bool WriteMessages(Span<byte> target, IReadOnlyList<Message> messages)
        {
            var size = MeasureMessages(messages);
            if(target.Length < size)
            {
                return false;
            }

            int cursor = 0;
            target[cursor] = (byte)'T';
            cursor++;

            foreach(var message in messages)
            {
                WriteMessage(target, message, ref cursor);
            }
            return true;
        }

        private static void WriteMessage(Span<byte> target, Message message, ref int cursor)
        {
            // Write the length as a UTF-8 string
            var len = message.Payload.Buffer.Length.ToString();
            var buf = Encoding.UTF8.GetBytes(len);
            buf.CopyTo(target.Slice(cursor, buf.Length));
            cursor += buf.Length;

            // Separator
            target[cursor] = (byte)':';
            cursor++;

            // Write the type character
            target[cursor] = GetTypeCharacter(message.Type);
            cursor++;

            // Separator
            target[cursor] = (byte)':';
            cursor++;

            // Payload
            if(message.Type == MessageType.Binary)
            {
                var payload = Encoding.UTF8.GetBytes(Convert.ToBase64String(message.Payload.Buffer.ToArray()));
                payload.CopyTo(target.Slice(cursor, payload.Length));
                cursor += payload.Length;
            } else
            {
                message.Payload.Buffer.CopyTo(target.Slice(cursor, message.Payload.Buffer.Length));
                cursor += message.Payload.Buffer.Length;
            }

            // Terminator
            target[cursor] = (byte)';';
            cursor++;
        }

        public static int MeasureMessages(IReadOnlyList<Message> messages)
        {
            var size = 1; // 1 character for the 'T'
            foreach(var message in messages)
            {
                size += MeasureMessage(message);
            }
            return size;
        }

        private static int MeasureMessage(Message message)
        {
            var size = 4; // two ':', one ';' and the type flag

            // Add enough characters for the payload
            var len = message.Type == MessageType.Binary ?
                // Length is measured as Base-64 character length
                (int)(4 * Math.Ceiling(((double)message.Payload.Buffer.Length / 3))) :
                // Length is just number of bytes in the (already UTF-8 encoded) payload
                message.Payload.Buffer.Length;
            size += len;

            // Add enough character to represent the length in digits
            // Log10(0) == Infinity, so don't allow that to happen.
            var digits = len == 0 ? 1 : (int)Math.Floor(Math.Log10(len)) + 1;
            size += digits;
            return size;
        }

        private static PreservedBuffer ParsePayload(ReadOnlySpan<byte> data, int length, MessageType messageType, ref int cursor)
        {
            int start = cursor;

            // We know exactly where the end is. The last byte is cursor + length
            cursor += length;

            // Verify the length and trailer
            if (cursor >= data.Length)
            {
                throw new FormatException("Unexpected end-of-message while reading Payload field.");
            }
            if (data[cursor] != ';')
            {
                throw new FormatException("Payload is missing trailer character ';'.");
            }

            // Read the data into a buffer
            var buffer = new byte[length];
            data.Slice(start, length).CopyTo(buffer);

            // If the message is binary, we need to convert from Base64
            if (messageType == MessageType.Binary)
            {
                // TODO: Use System.Binary.Base64 to handle this with less allocation

                // Parse the data as Base64
                var str = Encoding.UTF8.GetString(buffer);
                buffer = Convert.FromBase64String(str);
            }

            return ReadableBuffer.Create(buffer).Preserve();
        }

        private static MessageType ParseType(ReadOnlySpan<byte> data, ref int cursor)
        {
            int start = cursor;

            // Scan to a ':'
            cursor = IndexOf((byte)':', data, cursor);
            if (cursor >= data.Length)
            {
                throw new FormatException("Unexpected end-of-message while reading Type field.");
            }

            if (cursor - start != 1)
            {
                throw new FormatException("Type field must be exactly one byte long.");
            }

            switch (data[cursor - 1])
            {
                case (byte)'T': return MessageType.Text;
                case (byte)'B': return MessageType.Binary;
                case (byte)'C': return MessageType.Close;
                case (byte)'E': return MessageType.Error;
                default: throw new FormatException($"Unknown Type value: '{(char)data[cursor - 1]}'.");
            }
        }

        private static int ParseLength(ReadOnlySpan<byte> data, ref int cursor)
        {
            int start = cursor;

            // Scan to a ':'
            cursor = IndexOf((byte)':', data, cursor);
            if (cursor >= data.Length)
            {
                throw new FormatException("Unexpected end-of-message while reading Length field.");
            }

            // Parse the length
            int length = 0;
            for (int i = start; i < cursor; i++)
            {
                if (data[i] < '0' || data[i] > '9')
                {
                    throw new FormatException("Invalid length.");
                }
                length = (length * 10) + (data[i] - '0');
            }

            return length;
        }

        // There isn't a Span.IndexOf that takes a start point :(.
        private static int IndexOf(byte c, ReadOnlySpan<byte> data, int start)
        {
            // Scan to the end or to the matching character
            int cursor = start;
            for (; cursor < data.Length && data[cursor] != c; cursor++) ;
            return cursor;
        }

        private static byte GetTypeCharacter(MessageType type)
        {
            switch (type)
            {
                case MessageType.Text: return (byte)'T';
                case MessageType.Binary: return (byte)'B';
                case MessageType.Close: return (byte)'C';
                case MessageType.Error: return (byte)'E';
                default: throw new InvalidOperationException($"Unknown message type: {type}");
            }
        }
    }
}
