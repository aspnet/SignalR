// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.Collections;
using System.IO;
using System.Text;
using Microsoft.AspNetCore.SignalR.Internal.Formatters;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.AspNetCore.SignalR.Internal.Protocol
{
    public static class NegotiationProtocol
    {
        private static readonly UTF8Encoding _utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        private const string ProtocolPropertyName = "protocol";
        private const string ErrorPropertyName = "error";

        public static void WriteRequestMessage(NegotiationRequestMessage negotiationMessage, Stream output)
        {
            using (var writer = new JsonTextWriter(new StreamWriter(output, _utf8NoBom, 1024, leaveOpen: true)))
            {
                writer.WriteStartObject();
                writer.WritePropertyName(ProtocolPropertyName);
                writer.WriteValue(negotiationMessage.Protocol);
                writer.WriteEndObject();
            }

            TextMessageFormatter.WriteRecordSeparator(output);
        }

        public static void WriteResponseMessage(NegotiationResponseMessage negotiationResponseMessage, Stream output)
        {
            using (var writer = new JsonTextWriter(new StreamWriter(output, _utf8NoBom, 1024, leaveOpen: true)))
            {
                writer.WriteStartObject();
                if (!string.IsNullOrEmpty(negotiationResponseMessage.Error))
                {
                    writer.WritePropertyName(ErrorPropertyName);
                    writer.WriteValue(negotiationResponseMessage.Error);
                }
                writer.WriteEndObject();
            }

            TextMessageFormatter.WriteRecordSeparator(output);
        }

        public static bool TryParseResponseMessage(ReadOnlySpan<byte> input, out NegotiationResponseMessage negotiationMessage)
        {
            if (!TextMessageParser.TryParseMessage(ref input, out var payload))
            {
                throw new InvalidDataException("Unable to parse payload as a negotiation message.");
            }

            using (var memoryStream = new MemoryStream(payload.ToArray()))
            {
                using (var reader = new JsonTextReader(new StreamReader(memoryStream)))
                {
                    var token = JToken.ReadFrom(reader);
                    if (token == null || token.Type != JTokenType.Object)
                    {
                        throw new InvalidDataException($"Unexpected JSON Token Type '{token?.Type}'. Expected a JSON Object.");
                    }

                    var negotiationJObject = (JObject)token;
                    var error = JsonUtils.GetOptionalProperty<string>(negotiationJObject, ErrorPropertyName);
                    negotiationMessage = new NegotiationResponseMessage(error);
                }
            }
            return true;
        }

        public static bool TryParseResponseMessage(ReadOnlySequence<byte> buffer, out NegotiationResponseMessage negotiationMessage, out SequencePosition consumed, out SequencePosition examined)
        {
            var separator = buffer.PositionOf(TextMessageFormatter.RecordSeparator);
            if (separator == null)
            {
                // Haven't seen the entire negotiate message so bail
                consumed = buffer.Start;
                examined = buffer.End;
                negotiationMessage = null;
                return false;
            }
            else
            {
                consumed = buffer.GetPosition(1, separator.Value);
                examined = consumed;
            }

            var memory = buffer.IsSingleSegment ? buffer.First : buffer.ToArray();

            return TryParseResponseMessage(memory.Span, out negotiationMessage);
        }

        public static bool TryParseRequestMessage(ReadOnlySpan<byte> input, out NegotiationRequestMessage negotiationMessage)
        {
            if (!TextMessageParser.TryParseMessage(ref input, out var payload))
            {
                throw new InvalidDataException("Unable to parse payload as a negotiation message.");
            }

            using (var memoryStream = new MemoryStream(payload.ToArray()))
            {
                using (var reader = new JsonTextReader(new StreamReader(memoryStream)))
                {
                    var token = JToken.ReadFrom(reader);
                    if (token == null || token.Type != JTokenType.Object)
                    {
                        throw new InvalidDataException($"Unexpected JSON Token Type '{token?.Type}'. Expected a JSON Object.");
                    }

                    var negotiationJObject = (JObject)token;
                    var protocol = JsonUtils.GetRequiredProperty<string>(negotiationJObject, ProtocolPropertyName);
                    negotiationMessage = new NegotiationRequestMessage(protocol);
                }
            }
            return true;
        }

        public static bool TryParseRequestMessage(ReadOnlySequence<byte> buffer, out NegotiationRequestMessage negotiationMessage, out SequencePosition consumed, out SequencePosition examined)
        {
            var separator = buffer.PositionOf(TextMessageFormatter.RecordSeparator);
            if (separator == null)
            {
                // Haven't seen the entire negotiate message so bail
                consumed = buffer.Start;
                examined = buffer.End;
                negotiationMessage = null;
                return false;
            }
            else
            {
                consumed = buffer.GetPosition(1, separator.Value);
                examined = consumed;
            }

            var memory = buffer.IsSingleSegment ? buffer.First : buffer.ToArray();

            return TryParseRequestMessage(memory.Span, out negotiationMessage);
        }
    }
}
