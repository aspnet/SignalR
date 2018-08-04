// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Text.JsonLab;
using Microsoft.AspNetCore.Internal;
using Microsoft.AspNetCore.SignalR.Internal;
using Newtonsoft.Json;

namespace Microsoft.AspNetCore.SignalR.Protocol
{
    /// <summary>
    /// A helper class for working with SignalR handshakes.
    /// </summary>
    public static class HandshakeProtocol
    {
        private const string ProtocolPropertyName = "protocol";
        private const string ProtocolVersionPropertyName = "version";
        private const string MinorVersionPropertyName = "minorVersion";
        private const string ErrorPropertyName = "error";
        private const string TypePropertyName = "type";

        private static readonly byte[] ProtocolPropertyNameUtf8 = Encoding.UTF8.GetBytes("protocol");
        private static readonly byte[] ProtocolVersionPropertyNameUtf8 = Encoding.UTF8.GetBytes("version");
        private static readonly byte[] MinorVersionPropertyNameUtf8 = Encoding.UTF8.GetBytes("minorVersion");
        private static readonly byte[] ErrorPropertyNameUtf8 = Encoding.UTF8.GetBytes("error");
        private static readonly byte[] TypePropertyNameUtf8 = Encoding.UTF8.GetBytes("type");

        private static ConcurrentDictionary<IHubProtocol, ReadOnlyMemory<byte>> _messageCache = new ConcurrentDictionary<IHubProtocol, ReadOnlyMemory<byte>>();

        public static ReadOnlySpan<byte> GetSuccessfulHandshake(IHubProtocol protocol)
        {
            ReadOnlyMemory<byte> result;
            if(!_messageCache.TryGetValue(protocol, out result))
            {
                var memoryBufferWriter = MemoryBufferWriter.Get();
                try
                {
                    WriteResponseMessage(new HandshakeResponseMessage(protocol.MinorVersion), memoryBufferWriter);
                    result = memoryBufferWriter.ToArray();
                    _messageCache.TryAdd(protocol, result);
                }
                finally
                {
                    MemoryBufferWriter.Return(memoryBufferWriter);
                }
            }

            return result.Span;
        }

        /// <summary>
        /// Writes the serialized representation of a <see cref="HandshakeRequestMessage"/> to the specified writer.
        /// </summary>
        /// <param name="requestMessage">The message to write.</param>
        /// <param name="output">The output writer.</param>
        public static void WriteRequestMessage(HandshakeRequestMessage requestMessage, IBufferWriter<byte> output)
        {
            Utf8JsonWriter<IBufferWriter<byte>> writer = Utf8JsonWriter.Create(output);
            writer.WriteObjectStart();
            writer.WriteAttribute(ProtocolPropertyName, requestMessage.Protocol);
            writer.WriteAttribute(ProtocolVersionPropertyName, requestMessage.Version);
            writer.WriteObjectEnd();
            writer.Flush();

            TextMessageFormatter.WriteRecordSeparator(output);
        }

        /// <summary>
        /// Writes the serialized representation of a <see cref="HandshakeResponseMessage"/> to the specified writer.
        /// </summary>
        /// <param name="responseMessage">The message to write.</param>
        /// <param name="output">The output writer.</param>
        public static void WriteResponseMessage(HandshakeResponseMessage responseMessage, IBufferWriter<byte> output)
        {
            Utf8JsonWriter<IBufferWriter<byte>> writer = Utf8JsonWriter.Create(output);

            writer.WriteObjectStart();
            if (!string.IsNullOrEmpty(responseMessage.Error))
            {
                writer.WriteAttribute(ErrorPropertyName, responseMessage.Error);
            }

            writer.WriteAttribute(MinorVersionPropertyName, responseMessage.MinorVersion);

            writer.WriteObjectEnd();
            writer.Flush();

            TextMessageFormatter.WriteRecordSeparator(output);
        }

        /// <summary>
        /// Creates a new <see cref="HandshakeResponseMessage"/> from the specified serialized representation.
        /// </summary>
        /// <param name="buffer">The serialized representation of the message.</param>
        /// <param name="responseMessage">When this method returns, contains the parsed message.</param>
        /// <returns>A value that is <c>true</c> if the <see cref="HandshakeResponseMessage"/> was successfully parsed; otherwise, <c>false</c>.</returns>
        public static bool TryParseResponseMessage(ref ReadOnlySequence<byte> buffer, out HandshakeResponseMessage responseMessage)
        {
            if (!TextMessageParser.TryParseMessage(ref buffer, out var payload))
            {
                responseMessage = null;
                return false;
            }

            var reader = new Utf8JsonReader(payload);

            byte[] temp = payload.ToArray();
            string payStr = Encoding.UTF8.GetString(temp);

            JsonUtils.CheckRead(ref reader);
            JsonUtils.EnsureObjectStart(ref reader);

            int? minorVersion = null;
            string error = null;

            var completed = false;
            while (!completed && JsonUtils.CheckRead(ref reader))
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.PropertyName:
                        ReadOnlySpan<byte> memberName = reader.Value;

                        if (memberName.SequenceEqual(TypePropertyNameUtf8))
                        {

                            // a handshake response does not have a type
                            // check the incoming message was not any other type of message
                            throw new InvalidDataException("Handshake response should not have a 'type' value.");
                        }
                        else if (memberName.SequenceEqual(ErrorPropertyNameUtf8))
                        {
                            error = JsonUtils.ReadAsString(ref reader, ErrorPropertyName);
                        }
                        else if (memberName.SequenceEqual(MinorVersionPropertyNameUtf8))
                        {
                            minorVersion = JsonUtils.ReadAsInt32(ref reader, MinorVersionPropertyName);
                        }
                        else
                        {
                            reader.Skip();
                        }
                        break;
                    case JsonTokenType.EndObject:
                        completed = true;
                        break;
                    default:
                        throw new InvalidDataException($"Unexpected token '{reader.TokenType}' when reading handshake response JSON.");
                }
            };

            responseMessage = new HandshakeResponseMessage(minorVersion, error);
            return true;
        }

        /// <summary>
        /// Creates a new <see cref="HandshakeRequestMessage"/> from the specified serialized representation.
        /// </summary>
        /// <param name="buffer">The serialized representation of the message.</param>
        /// <param name="requestMessage">When this method returns, contains the parsed message.</param>
        /// <returns>A value that is <c>true</c> if the <see cref="HandshakeRequestMessage"/> was successfully parsed; otherwise, <c>false</c>.</returns>
        public static bool TryParseRequestMessage(ref ReadOnlySequence<byte> buffer, out HandshakeRequestMessage requestMessage)
        {
            if (!TextMessageParser.TryParseMessage(ref buffer, out var payload))
            {
                requestMessage = null;
                return false;
            }

            var reader = new Utf8JsonReader(payload);
            JsonUtils.CheckRead(ref reader);
            JsonUtils.EnsureObjectStart(ref reader);

            string protocol = null;
            int? protocolVersion = null;

            var completed = false;
            while (!completed && JsonUtils.CheckRead(ref reader))
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.PropertyName:
                        ReadOnlySpan<byte> memberName = reader.Value;

                        if (memberName.SequenceEqual(ProtocolPropertyNameUtf8))
                        {
                            protocol = JsonUtils.ReadAsString(ref reader, ProtocolPropertyName);
                        }
                        else if (memberName.SequenceEqual(ProtocolVersionPropertyNameUtf8))
                        {
                            protocolVersion = JsonUtils.ReadAsInt32(ref reader, ProtocolVersionPropertyName);
                        }
                        else
                        {
                            reader.Skip();
                        }
                        break;
                    case JsonTokenType.EndObject:
                        completed = true;
                        break;
                    default:
                        throw new InvalidDataException($"Unexpected token '{reader.TokenType}' when reading handshake request JSON.");
                }
            }

            if (protocol == null)
            {
                throw new InvalidDataException($"Missing required property '{ProtocolPropertyName}'.");
            }
            if (protocolVersion == null)
            {
                throw new InvalidDataException($"Missing required property '{ProtocolVersionPropertyName}'.");
            }

            requestMessage = new HandshakeRequestMessage(protocol, protocolVersion.Value);

            return true;
        }
    }
}