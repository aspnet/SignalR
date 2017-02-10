using System;
using System.Collections.Generic;

namespace Microsoft.AspNetCore.Sockets
{
    public static class MessageBatchFormatter
    {
        private const byte BatchTerminator = (byte)';';
        private const byte TextFormatIndicator = (byte)'T';
        private const byte BinaryFormatIndicator = (byte)'B';

        public static bool TryFormatMessages(IEnumerable<Message> messages, Span<byte> buffer, MessageFormat format, out int bytesWritten)
        {
            // Write the format discriminator
            if (!TryFormatMessageFormat(format, buffer, out var consumedByFormat))
            {
                bytesWritten = 0;
                return false;
            }
            buffer = buffer.Slice(consumedByFormat);

            // Write messages
            var writtenSoFar = 1;
            foreach (var message in messages)
            {
                if (!TryFormatMessage(message, buffer, format, out var writtenForMessage))
                {
                    bytesWritten = 0;
                    return false;
                }
                writtenSoFar += writtenForMessage;
                buffer = buffer.Slice(writtenForMessage);
            }

            if(buffer.Length < 1)
            {
                bytesWritten = 0;
                return false;
            }
            buffer[0] = BatchTerminator;
            bytesWritten = writtenSoFar + 1;
            return true;
        }

        public static bool TryParseMessages(ReadOnlySpan<byte> buffer, out IList<Message> messages, out int bytesConsumed)
        {
            if(!TryParseMessageFormat(buffer, out var messageFormat, out var consumedForFormat))
            {
                // Batch is missing the prefix
                bytesConsumed = 0;
                messages = new List<Message>();
                return false;
            }
            var consumedSoFar = consumedForFormat;
            buffer = buffer.Slice(consumedForFormat);

            var readMessages = new List<Message>();
            while (TryParseMessage(buffer, messageFormat, out Message message, out int consumedForMessage))
            {
                readMessages.Add(message);
                consumedSoFar += consumedForMessage;
                buffer = buffer.Slice(consumedForMessage);
            }

            if (buffer.Length < 1 || buffer[0] != BatchTerminator)
            {
                // Batch is invalid, missing the terminator
                bytesConsumed = 0;
                messages = new List<Message>();
                return false;
            }

            bytesConsumed = consumedSoFar + 1;
            messages = readMessages;
            return true;
        }

        private static bool TryParseMessageFormat(ReadOnlySpan<byte> buffer, out MessageFormat format, out int bytesConsumed)
        {
            if (buffer.Length >= 1)
            {
                if (buffer[0] == TextFormatIndicator)
                {
                    bytesConsumed = 1;
                    format = MessageFormat.Text;
                    return true;
                }
                else if (buffer[0] == BinaryFormatIndicator)
                {
                    bytesConsumed = 1;
                    format = MessageFormat.Binary;
                    return true;
                }
            }

            bytesConsumed = 0;
            format = MessageFormat.Binary;
            return false;
        }

        private static bool TryFormatMessageFormat(MessageFormat format, Span<byte> buffer, out int bytesWritten)
        {
            switch (format)
            {
                case MessageFormat.Text:
                    buffer[0] = TextFormatIndicator;
                    break;
                case MessageFormat.Binary:
                    buffer[0] = BinaryFormatIndicator;
                    break;
                default:
                    bytesWritten = 0;
                    return false;
            }

            bytesWritten = 1;
            return true;
        }

        private static bool TryFormatMessage(Message message, Span<byte> buffer, MessageFormat format, out int bytesWritten)
        {
            return format == MessageFormat.Text ?
                TextMessageFormatter.TryFormatMessage(message, buffer, out bytesWritten) :
                throw new NotImplementedException();
        }

        private static bool TryParseMessage(ReadOnlySpan<byte> buffer, MessageFormat format, out Message message, out int bytesConsumed)
        {
            return format == MessageFormat.Text ?
                TextMessageFormatter.TryParseMessage(buffer, out message, out bytesConsumed) :
                throw new NotImplementedException();
        }
    }
}
