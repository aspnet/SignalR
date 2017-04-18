// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.using System;

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Text;

namespace Microsoft.AspNetCore.Sockets.Internal.Formatters
{
    public class ServerSentEventsMessageParser
    {
        static readonly byte ByteCR = (byte)'\r';
        static readonly byte ByteLF = (byte)'\n';

        private InternalParseState _internalParserState = InternalParseState.ReadMessageType;
        private IList<byte[]> _data = new List<byte[]>();

        public ParseResult ParseMessage(ReadableBuffer buffer, out ReadCursor consumed, out ReadCursor examined, out Message message)
        { 
            consumed = buffer.Start;
            examined = buffer.End;
            message = new Message();
            var messageType = MessageType.Text;
            var reader = new ReadableBufferReader(buffer);

            var start = consumed;
            var end = examined;

            while (!reader.End)
            {
                if (ReadCursorOperations.Seek(start, end, out var lineEnd, ByteLF) == -1)
                {
                    //Partial message. We need to read more.
                    return ParseResult.Incomplete;
                }

                lineEnd = buffer.Move(lineEnd, 1);
                var line = ConvertBufferToSpan(buffer.Slice(start, lineEnd));
                reader.Skip(line.Length);

                //Check for a misplaced '\n'
                if (line.Length <= 1 || line[line.Length - 2] != ByteCR)
                {
                    throw new FormatException("There was an issue with the frame format");
                }

                //Strip the \r\n from the span
                line = line.Slice(0, line.Length - 2);

                switch (_internalParserState)
                {
                    case InternalParseState.ReadMessageType:
                        messageType = GetMessageType(line);
                        start = lineEnd;
                        _internalParserState = InternalParseState.ReadMessagePayload;
                        consumed = lineEnd;

                        break;
                    case InternalParseState.ReadMessagePayload:
                        if (!StartsWithDataPrefix(line))
                        {
                            throw new FormatException("Expected the message prefix 'data: '");
                        }

                        //Slice away the 'data: '
                        var newData = line.Slice(6).ToArray();

                        start = lineEnd;
                        _data.Add(newData);
                        consumed = lineEnd;
                        break;
                    case InternalParseState.ReadEndOfMessage:
                        if (ReadCursorOperations.Seek(start, end, out lineEnd, ByteLF) == -1)
                        {
                            // The message has ended with \r\n\r
                            return ParseResult.Incomplete;
                        }

                        if (_data.Count > 0)
                        {
                            //Find the final size of the payload
                            var payloadSize = 0;
                            foreach (var dataLine in _data)
                            {
                                payloadSize += dataLine.Length;
                            }
                            var payload = new byte[payloadSize];

                            //Copy the contents of the data array to a single buffer
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
                            //Empty message
                            message = new Message(Array.Empty<byte>(), messageType);
                        }

                        consumed = buffer.End;
                        return ParseResult.Completed;
                }

                //Peek into next byte. If it is a carriage return byte, then advance to the next state
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

        private bool StartsWithDataPrefix(ReadOnlySpan<byte> line)
        {
            var dataPrefix = Encoding.UTF8.GetBytes("data: ");
            var prefixSpan = new ReadOnlySpan<byte>(dataPrefix);
            if (line.StartsWith(dataPrefix))
            {
                return true;
            }

            return false;
        }

        private MessageType GetMessageType(ReadOnlySpan<byte> line)
        {
            if (!StartsWithDataPrefix(line))
            {
                throw new FormatException("Expected the message prefix 'data: '");
            }

            if (line.Length != 7)
            {
                throw new FormatException("There was an error parsing the message type");
            }

            //Skip the "data: " part of the line
            var type = (char)line[6];
            switch (type)
            {
                case 'T':
                    return MessageType.Text;
                case 'B':
                    return MessageType.Binary;
                case 'C':
                    return MessageType.Close;
                case 'E':
                    return MessageType.Error;
                default:
                    throw new FormatException($"Unknown message type: '{type}'");
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
