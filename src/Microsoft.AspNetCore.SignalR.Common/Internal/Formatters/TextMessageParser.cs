// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO.Pipelines;
using System.Text;

namespace Microsoft.AspNetCore.Sockets.Internal.Formatters
{
    public static class TextMessageParser
    {
        private const int Int32OverflowLength = 10;

        /// <summary>
        /// Attempts to parse a message from the buffer. Returns 'false' if there is not enough data to complete a message. Throws an
        /// exception if there is a format error in the provided data.
        /// </summary>
        public static bool TryParseMessage(ReadableBuffer input, out ReadCursor consumed, out ReadCursor examined, out ReadableBuffer payload)
        {
            consumed = input.Start;
            examined = input.End;
            payload = default(ReadableBuffer);

            // {length}:{payload};
            if (!TryReadLength(input, out var found, out var length))
            {
                return false;
            }

            var start = input.Move(found, 1);
            var remaining = input.Slice(start);

            // Expect payload + 1
            if (remaining.Length < length + 1)
            {
                return false;
            }

            // Payload
            payload = remaining.Slice(remaining.Start, length);

            // .... This is pretty awful
            var span = input.Slice(payload.Move(payload.End, 1), 1).First.Span;

            // Verify the next character is TextMessageFormatter.MessageDelimiter
            if (span[0] != (byte)TextMessageFormatter.MessageDelimiter)
            {
                throw new FormatException($"Missing delimiter '{TextMessageFormatter.MessageDelimiter}' after payload");
            }

            consumed = payload.End;
            examined = payload.End;

            return true;
        }

        private static bool TryReadLength(ReadableBuffer input, out ReadCursor found, out int length)
        {
            // Read until the first ':' to find the length
            if (ReadCursorOperations.Seek(input.Start, input.End, out found, (byte)TextMessageFormatter.MessageDelimiter) == -1)
            {
                // Insufficient data
                length = 0;
                return false;
            }

            var lengthBuffer = input.Slice(input.Start, found).ToArray();

            if (!TryParseInt32(lengthBuffer, out length, out var bytesConsumed) || bytesConsumed < lengthBuffer.Length)
            {
                throw new FormatException($"Invalid length: '{Encoding.UTF8.GetString(lengthBuffer)}'");
            }

            return true;
        }

        private static bool TryParseInt32(ReadOnlySpan<byte> text, out int value, out int bytesConsumed)
        {
            if (text.Length < 1)
            {
                bytesConsumed = 0;
                value = default(int);
                return false;
            }

            int indexOfFirstDigit = 0;
            int sign = 1;
            if (text[0] == '-')
            {
                indexOfFirstDigit = 1;
                sign = -1;
            }
            else if (text[0] == '+')
            {
                indexOfFirstDigit = 1;
            }

            int overflowLength = Int32OverflowLength + indexOfFirstDigit;

            // Parse the first digit separately. If invalid here, we need to return false.
            int firstDigit = text[indexOfFirstDigit] - 48; // '0'
            if (firstDigit < 0 || firstDigit > 9)
            {
                bytesConsumed = 0;
                value = default(int);
                return false;
            }
            int parsedValue = firstDigit;

            if (text.Length < overflowLength)
            {
                // Length is less than Int32OverflowLength; overflow is not possible
                for (int index = indexOfFirstDigit + 1; index < text.Length; index++)
                {
                    int nextDigit = text[index] - 48; // '0'
                    if (nextDigit < 0 || nextDigit > 9)
                    {
                        bytesConsumed = index;
                        value = parsedValue * sign;
                        return true;
                    }
                    parsedValue = parsedValue * 10 + nextDigit;
                }
            }
            else
            {
                // Length is greater than Int32OverflowLength; overflow is only possible after Int32OverflowLength
                // digits. There may be no overflow after Int32OverflowLength if there are leading zeroes.
                for (int index = indexOfFirstDigit + 1; index < overflowLength - 1; index++)
                {
                    int nextDigit = text[index] - 48; // '0'
                    if (nextDigit < 0 || nextDigit > 9)
                    {
                        bytesConsumed = index;
                        value = parsedValue * sign;
                        return true;
                    }
                    parsedValue = parsedValue * 10 + nextDigit;
                }
                for (int index = overflowLength - 1; index < text.Length; index++)
                {
                    int nextDigit = text[index] - 48; // '0'
                    if (nextDigit < 0 || nextDigit > 9)
                    {
                        bytesConsumed = index;
                        value = parsedValue * sign;
                        return true;
                    }
                    // If parsedValue > (int.MaxValue / 10), any more appended digits will cause overflow.
                    // if parsedValue == (int.MaxValue / 10), any nextDigit greater than 7 or 8 (depending on sign) implies overflow.
                    bool positive = sign > 0;
                    bool nextDigitTooLarge = nextDigit > 8 || (positive && nextDigit > 7);
                    if (parsedValue > int.MaxValue / 10 || parsedValue == int.MaxValue / 10 && nextDigitTooLarge)
                    {
                        bytesConsumed = 0;
                        value = default(int);
                        return false;
                    }
                    parsedValue = parsedValue * 10 + nextDigit;
                }
            }

            bytesConsumed = text.Length;
            value = parsedValue * sign;
            return true;
        }
    }
}
