using System;
using System.Collections.Generic;

namespace Microsoft.AspNetCore.Sockets
{
    public static class MessageFormatter
    {
        public static bool TryFormatMessage(Message message, Span<byte> buffer, MessageFormat format, out int bytesWritten)
        {
            if(!message.EndOfMessage)
            {
                // This is a truely exceptional condition since we EXPECT callers to have already
                // buffered incomplete messages and synthesized the correct, complete message before
                // giving it to us. Hence we throw, instead of returning false.
                throw new InvalidOperationException("Cannot format message where endOfMessage is false using this format");
            }
            return format == MessageFormat.Text ?
                TextMessageFormatter.TryFormatMessage(message, buffer, out bytesWritten) :
                throw new NotImplementedException();
        }

        public static bool TryParseMessage(ReadOnlySpan<byte> buffer, MessageFormat format, out Message message, out int bytesConsumed)
        {
            return format == MessageFormat.Text ?
                TextMessageFormatter.TryParseMessage(buffer, out message, out bytesConsumed) :
                throw new NotImplementedException();
        }
    }
}
