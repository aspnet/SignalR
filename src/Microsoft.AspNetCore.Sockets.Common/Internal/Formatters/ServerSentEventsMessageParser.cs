// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.using System;

using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Text;

namespace Microsoft.AspNetCore.Sockets.Internal.Formatters
{
    public class ServerSentEventsMessageParser
    {
        const byte ByteCR = (byte)'\r';
        const byte ByteLF = (byte)'\n';

        const byte ByteT = (byte)'T';
        const byte ByteB = (byte)'B';
        const byte ByteC = (byte)'C';
        const byte ByteE = (byte)'E';

        private static byte[] _dataPrefix = Encoding.UTF8.GetBytes("data: ");

        private InternalParseState _internalParserState = InternalParseState.ReadMessageType;
        private List<byte[]> _data = new List<byte[]>();

        public ParseResult ParseMessage(ReadableBuffer buffer, out ReadCursor consumed, out ReadCursor examined, out Message message)
        {
            consumed = buffer.Start;
            examined = buffer.End;
            message = new Message();
            var reader = new ReadableBufferReader(buffer);

            var start = consumed;
            var end = examined;

            while (!reader.End)
            {
                var messageType = MessageType.Text;

                if (ReadCursorOperations.Seek(start, end, out var lineEnd, ByteLF) == -1)
                {
                    // Partial message. We need to read more.
                    return ParseResult.Incomplete;
                }

                lineEnd = buffer.Move(lineEnd, 1);
                var line = ConvertBufferToSpan(buffer.Slice(start, lineEnd));
                reader.Skip(line.Length);

                // Check for a misplaced '\n'
                if (line.Length <= 1)
                {
                    throw new FormatException("There was an error in the frame format");
                }

                // To ensure that the \n was preceded by a \r
                // since messages can't contain \n.
                // data: foo\n\bar should be encoded as
                // data: foo\r\n
                // data: bar\r\n
                if (line[line.Length - 2] != ByteCR)
                {
                    throw new FormatException("A '\\n' character can only be used as a line ending");
                }

                // Remove the \r\n from the span
                line = line.Slice(0, line.Length - 2);

                switch (_internalParserState)
                {
                    case InternalParseState.ReadMessageType:
                        messageType = GetMessageType(line);
                        start = lineEnd;
                        consumed = lineEnd;
                        _internalParserState = InternalParseState.ReadMessagePayload;

                        break;
                    case InternalParseState.ReadMessagePayload:
                        EnsureStartsWithDataPrefix(line);

                        // Slice away the 'data: '
                        var newData = line.Slice(_dataPrefix.Length).ToArray();

                        start = lineEnd;
                        consumed = lineEnd;
                        _data.Add(newData);
                        break;
                    case InternalParseState.ReadEndOfMessage:
                        if (ReadCursorOperations.Seek(start, end, out lineEnd, ByteLF) == -1)
                        {
                            // The message has ended with \r\n\r
                            return ParseResult.Incomplete;
                        }

                        if (_data.Count > 0)
                        {
                            // Find the final size of the payload
                            var payloadSize = 0;
                            foreach (var dataLine in _data)
                            {
                                payloadSize += dataLine.Length;
                            }
                            var payload = new byte[payloadSize];

                            // Copy the contents of the data array to a single buffer
                            var offset = 0;
                            foreach (var dataLine in _data)
                            {
                                dataLine.CopyTo(payload, offset);
                                offset += dataLine.Length;
                            }

                            message = new Message(payload, messageType);
                        }
                        else
                        {
                            // Empty message
                            message = new Message(Array.Empty<byte>(), messageType);
                        }

                        consumed = lineEnd;
                        return ParseResult.Completed;
                }

                // Peek into next byte. If it is a carriage return byte, then advance to the next state
                if (reader.Peek() == ByteCR)
                {
                    _internalParserState = InternalParseState.ReadEndOfMessage;
                }
            }
            return ParseResult.Incomplete;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Span<byte> ConvertBufferToSpan(ReadableBuffer buffer)
        {
            if (buffer.IsSingleSpan)
            {
                return buffer.First.Span;
            }
            return buffer.ToArray();
        }

        public void Reset()
        {
            _internalParserState = InternalParseState.ReadMessageType;
            _data.Clear();
        }

        private void EnsureStartsWithDataPrefix(ReadOnlySpan<byte> line)
        {
            if (!line.StartsWith(_dataPrefix))
            {
                throw new FormatException("Expected the message prefix 'data: '");
            }
        }

        private MessageType GetMessageType(ReadOnlySpan<byte> line)
        {
            EnsureStartsWithDataPrefix(line);

            // Skip the "data: " part of the line
            var type = line[_dataPrefix.Length];
            switch (type)
            {
                case ByteT:
                    return MessageType.Text;
                case ByteB:
                    return MessageType.Binary;
                case ByteC:
                    return MessageType.Close;
                case ByteE:
                    return MessageType.Error;
                default:
                    throw new FormatException($"Unknown message type: '{(char)type}'");
            }
        }

        public enum ParseResult
        {
            Completed,
            Incomplete,
        }

        private enum InternalParseState
        {
            Initial,
            ReadMessageType,
            ReadMessagePayload,
            ReadEndOfMessage,
            Error
        }
    }
}
