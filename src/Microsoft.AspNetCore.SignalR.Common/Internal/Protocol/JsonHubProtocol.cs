// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Text;
using Microsoft.AspNetCore.Protocols;
using Microsoft.AspNetCore.SignalR.Internal.Formatters;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Microsoft.AspNetCore.SignalR.Internal.Protocol
{
    public class JsonHubProtocol : IHubProtocol
    {
        private static readonly UTF8Encoding _utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        private static readonly JsonSerializer _jsonSerializer = new JsonSerializer();

        private const string ResultPropertyName = "result";
        private const string ItemPropertyName = "item";
        private const string InvocationIdPropertyName = "invocationId";
        private const string TypePropertyName = "type";
        private const string ErrorPropertyName = "error";
        private const string TargetPropertyName = "target";
        private const string ArgumentsPropertyName = "arguments";
        private const string PayloadPropertyName = "payload";
        private const string HeadersPropertyName = "headers";

        public static readonly string ProtocolName = "json";

        // ONLY to be used for application payloads (args, return values, etc.)
        public JsonSerializer PayloadSerializer { get; }

        public JsonHubProtocol() : this(Options.Create(new JsonHubProtocolOptions()))
        {
        }

        public JsonHubProtocol(IOptions<JsonHubProtocolOptions> options)
        {
            PayloadSerializer = JsonSerializer.Create(options.Value.PayloadSerializerSettings);
        }

        public string Name => ProtocolName;

        public TransferFormat TransferFormat => TransferFormat.Text;

        public bool TryParseMessages(ReadOnlyMemory<byte> input, IInvocationBinder binder, IList<HubMessage> messages)
        {
            while (TextMessageParser.TryParseMessage(ref input, out var payload))
            {
                messages.Add(ParseMessage(payload, binder));
            }

            return messages.Count > 0;
        }

        public void WriteMessage(HubMessage message, Stream output)
        {
            WriteMessageCore(message, output);
            TextMessageFormatter.WriteRecordSeparator(output);
        }

        private HubMessage ParseMessage(ReadOnlyMemory<byte> input, IInvocationBinder binder)
        {
            try
            {
                // PERF: This is still inefficient, we can do this in a single pass with a much
                // more complicated state machine, instead, we're doing 2 passes.
                // The first pass is for the top level simple properties
                // The second pass then one to parse arguments and return types (if the message type requires that)

                int? type = null;
                string invocationId = null;
                string target = null;
                string error = null;
                Dictionary<string, string> headers = null;

                using (var reader = new JsonTextReader(new Utf8BufferTextReader(input)))
                {
                    reader.ArrayPool = JsonArrayPool<char>.Shared;

                    reader.Read();

                    // We're always parsing a JSON object
                    if (reader.TokenType != JsonToken.StartObject)
                    {
                        if (reader.TokenType == JsonToken.None)
                        {
                            throw new InvalidDataException("Error reading JSON.");
                        }

                        throw new InvalidDataException($"Unexpected JSON Token Type '{reader.TokenType}'. Expected a JSON Object.");
                    }

                    do
                    {
                        switch (reader.TokenType)
                        {
                            case JsonToken.PropertyName:
                                // We only care about parsing top level properties
                                if (reader.Depth == 1)
                                {
                                    string memberName = reader.Value.ToString();

                                    if (string.Equals(memberName, TypePropertyName, StringComparison.Ordinal))
                                    {
                                        var messageType = reader.ReadAsInt32();

                                        if (messageType == null)
                                        {
                                            throw new InvalidDataException($"Missing required property '{TypePropertyName}'.");
                                        }

                                        type = messageType.Value;
                                    }
                                    else if (string.Equals(memberName, InvocationIdPropertyName, StringComparison.Ordinal))
                                    {
                                        invocationId = reader.ReadAsString();
                                    }
                                    else if (string.Equals(memberName, TargetPropertyName, StringComparison.Ordinal))
                                    {
                                        target = reader.ReadAsString();
                                    }
                                    else if (string.Equals(memberName, ErrorPropertyName, StringComparison.Ordinal))
                                    {
                                        error = reader.ReadAsString();
                                    }
                                    else if (string.Equals(memberName, HeadersPropertyName, StringComparison.Ordinal))
                                    {
                                        reader.Read();
                                        headers = _jsonSerializer.Deserialize<Dictionary<string, string>>(reader);
                                    }
                                }

                                break;
                            default:
                                break;
                        }
                    }
                    while (reader.Read());
                }

                if (type == null)
                {
                    throw new InvalidDataException($"Missing required property '{TypePropertyName}'.");
                }

                // Now we have the message type, parse again
                // PERF: We could reset the existing Utf8BufferTextReader so we don't need to allocate twice
                using (var reader = new JsonTextReader(new Utf8BufferTextReader(input)))
                {
                    reader.ArrayPool = JsonArrayPool<char>.Shared;
                    reader.Read();

                    HubMessage message = null;

                    switch (type)
                    {
                        case HubProtocolConstants.InvocationMessageType:
                            message = BindInvocationMessage(reader, invocationId, target, binder);
                            break;
                        case HubProtocolConstants.StreamInvocationMessageType:
                            message = BindStreamInvocationMessage(reader, invocationId, target, binder);
                            break;
                        case HubProtocolConstants.StreamItemMessageType:
                            message = BindStreamItemMessage(reader, invocationId, binder);
                            break;
                        case HubProtocolConstants.CompletionMessageType:
                            message = BindCompletionMessage(reader, invocationId, error, binder);
                            break;
                        case HubProtocolConstants.CancelInvocationMessageType:
                            message = BindCancelInvocationMessage(reader, invocationId);
                            break;
                        case HubProtocolConstants.PingMessageType:
                            return PingMessage.Instance;
                        default:
                            throw new InvalidDataException($"Unknown message type: {type}");
                    }

                    return ApplyHeaders(message, headers);
                }
            }
            catch (JsonReaderException jrex)
            {
                throw new InvalidDataException("Error reading JSON.", jrex);
            }
        }

        private void WriteMessageCore(HubMessage message, Stream stream)
        {
            using (var writer = new JsonTextWriter(new StreamWriter(stream, _utf8NoBom, 1024, leaveOpen: true)))
            {
                writer.WriteStartObject();
                switch (message)
                {
                    case InvocationMessage m:
                        WriteMessageType(writer, HubProtocolConstants.InvocationMessageType);
                        WriteHeaders(writer, m);
                        WriteInvocationMessage(m, writer);
                        break;
                    case StreamInvocationMessage m:
                        WriteMessageType(writer, HubProtocolConstants.StreamInvocationMessageType);
                        WriteHeaders(writer, m);
                        WriteStreamInvocationMessage(m, writer);
                        break;
                    case StreamItemMessage m:
                        WriteMessageType(writer, HubProtocolConstants.StreamItemMessageType);
                        WriteHeaders(writer, m);
                        WriteStreamItemMessage(m, writer);
                        break;
                    case CompletionMessage m:
                        WriteMessageType(writer, HubProtocolConstants.CompletionMessageType);
                        WriteHeaders(writer, m);
                        WriteCompletionMessage(m, writer);
                        break;
                    case CancelInvocationMessage m:
                        WriteMessageType(writer, HubProtocolConstants.CancelInvocationMessageType);
                        WriteHeaders(writer, m);
                        WriteCancelInvocationMessage(m, writer);
                        break;
                    case PingMessage _:
                        WriteMessageType(writer, HubProtocolConstants.PingMessageType);
                        break;
                    default:
                        throw new InvalidOperationException($"Unsupported message type: {message.GetType().FullName}");
                }
                writer.WriteEndObject();
            }
        }

        private void WriteHeaders(JsonTextWriter writer, HubInvocationMessage message)
        {
            if (message.Headers != null && message.Headers.Count > 0)
            {
                writer.WritePropertyName(HeadersPropertyName);
                writer.WriteStartObject();
                foreach (var value in message.Headers)
                {
                    writer.WritePropertyName(value.Key);
                    writer.WriteValue(value.Value);
                }
                writer.WriteEndObject();
            }
        }

        private void WriteCompletionMessage(CompletionMessage message, JsonTextWriter writer)
        {
            WriteInvocationId(message, writer);
            if (!string.IsNullOrEmpty(message.Error))
            {
                writer.WritePropertyName(ErrorPropertyName);
                writer.WriteValue(message.Error);
            }
            else if (message.HasResult)
            {
                writer.WritePropertyName(ResultPropertyName);
                PayloadSerializer.Serialize(writer, message.Result);
            }
        }

        private void WriteCancelInvocationMessage(CancelInvocationMessage message, JsonTextWriter writer)
        {
            WriteInvocationId(message, writer);
        }

        private void WriteStreamItemMessage(StreamItemMessage message, JsonTextWriter writer)
        {
            WriteInvocationId(message, writer);
            writer.WritePropertyName(ItemPropertyName);
            PayloadSerializer.Serialize(writer, message.Item);
        }

        private void WriteInvocationMessage(InvocationMessage message, JsonTextWriter writer)
        {
            WriteInvocationId(message, writer);
            writer.WritePropertyName(TargetPropertyName);
            writer.WriteValue(message.Target);

            WriteArguments(message.Arguments, writer);
        }

        private void WriteStreamInvocationMessage(StreamInvocationMessage message, JsonTextWriter writer)
        {
            WriteInvocationId(message, writer);
            writer.WritePropertyName(TargetPropertyName);
            writer.WriteValue(message.Target);

            WriteArguments(message.Arguments, writer);
        }

        private void WriteArguments(object[] arguments, JsonTextWriter writer)
        {
            writer.WritePropertyName(ArgumentsPropertyName);
            writer.WriteStartArray();
            foreach (var argument in arguments)
            {
                PayloadSerializer.Serialize(writer, argument);
            }
            writer.WriteEndArray();
        }

        private static void WriteInvocationId(HubInvocationMessage message, JsonTextWriter writer)
        {
            if (!string.IsNullOrEmpty(message.InvocationId))
            {
                writer.WritePropertyName(InvocationIdPropertyName);
                writer.WriteValue(message.InvocationId);
            }
        }

        private static void WriteMessageType(JsonTextWriter writer, int type)
        {
            writer.WritePropertyName(TypePropertyName);
            writer.WriteValue(type);
        }

        private HubMessage BindCancelInvocationMessage(JsonTextReader reader, string invocationId)
        {
            if (string.IsNullOrEmpty(invocationId))
            {
                throw new InvalidDataException($"Missing required property '{InvocationIdPropertyName}'.");
            }

            return new CancelInvocationMessage(invocationId);
        }

        private HubMessage BindCompletionMessage(JsonTextReader reader, string invocationId, string error, IInvocationBinder binder)
        {
            if (string.IsNullOrEmpty(invocationId))
            {
                throw new InvalidDataException($"Missing required property '{InvocationIdPropertyName}'.");
            }

            object result = null;
            bool finished = false;

            do
            {
                switch (reader.TokenType)
                {
                    case JsonToken.PropertyName:
                        if (reader.Depth == 1)
                        {
                            string memberName = reader.Value.ToString();

                            if (string.Equals(memberName, ResultPropertyName, StringComparison.Ordinal))
                            {
                                reader.Read();
                                var returnType = binder.GetReturnType(invocationId);
                                result = PayloadSerializer.Deserialize(reader);
                                finished = true;
                            }
                        }
                        break;
                    default:
                        break;
                }
            }
            while (reader.Read() && !finished);

            // Finished means we have a result property
            if (error != null && finished)
            {
                throw new InvalidDataException("The 'error' and 'result' properties are mutually exclusive.");
            }

            if (finished)
            {
                return new CompletionMessage(invocationId, error, result, hasResult: true);
            }

            return new CompletionMessage(invocationId, error, result: null, hasResult: false);
        }

        private HubMessage BindStreamItemMessage(JsonTextReader reader, string invocationId, IInvocationBinder binder)
        {
            if (string.IsNullOrEmpty(invocationId))
            {
                throw new InvalidDataException($"Missing required property '{InvocationIdPropertyName}'.");
            }

            object item = null;
            bool finished = false;

            do
            {
                switch (reader.TokenType)
                {
                    case JsonToken.PropertyName:
                        if (reader.Depth == 1)
                        {
                            string memberName = reader.Value.ToString();

                            if (string.Equals(memberName, ItemPropertyName, StringComparison.Ordinal))
                            {
                                reader.Read();
                                var returnType = binder.GetReturnType(invocationId);
                                item = PayloadSerializer.Deserialize(reader);

                                finished = true;
                            }
                        }
                        break;
                    default:
                        break;
                }
            }
            while (reader.Read() && !finished);

            if (!finished)
            {
                throw new InvalidDataException($"Missing required property '{ItemPropertyName}'.");
            }

            return new StreamItemMessage(invocationId, item);
        }

        private HubMessage BindStreamInvocationMessage(JsonTextReader reader, string invocationId, string target, IInvocationBinder binder)
        {
            if (string.IsNullOrEmpty(invocationId))
            {
                throw new InvalidDataException($"Missing required property '{InvocationIdPropertyName}'.");
            }

            if (string.IsNullOrEmpty(target))
            {
                throw new InvalidDataException($"Missing required property '{TargetPropertyName}'.");
            }

            object[] arguments = null;
            ExceptionDispatchInfo argumentBindingException = null;
            bool finished = false;

            do
            {
                switch (reader.TokenType)
                {
                    case JsonToken.PropertyName:
                        if (reader.Depth == 1)
                        {
                            string memberName = reader.Value.ToString();

                            if (string.Equals(memberName, ArgumentsPropertyName, StringComparison.Ordinal))
                            {
                                reader.Read();

                                JsonUtils.EnsureTokenType(ArgumentsPropertyName, reader.TokenType, JsonToken.StartArray);

                                try
                                {
                                    var paramTypes = binder.GetParameterTypes(target);
                                    arguments = BindArguments(reader, paramTypes);
                                }
                                catch (Exception ex)
                                {
                                    argumentBindingException = ExceptionDispatchInfo.Capture(ex);
                                }
                                finished = true;
                            }
                        }
                        break;
                    default:
                        break;
                }
            }
            while (reader.Read() && !finished);

            if (!finished)
            {
                throw new InvalidDataException($"Missing required property '{ArgumentsPropertyName}'.");
            }

            return new StreamInvocationMessage(invocationId, target, argumentBindingException: argumentBindingException, arguments: arguments);
        }

        private HubMessage BindInvocationMessage(JsonTextReader reader, string invocationId, string target, IInvocationBinder binder)
        {
            if (string.IsNullOrEmpty(target))
            {
                throw new InvalidDataException($"Missing required property '{TargetPropertyName}'.");
            }

            object[] arguments = null;
            ExceptionDispatchInfo argumentBindingException = null;
            bool finished = false;

            do
            {
                switch (reader.TokenType)
                {
                    case JsonToken.PropertyName:
                        if (reader.Depth == 1)
                        {
                            string memberName = reader.Value.ToString();

                            if (string.Equals(memberName, ArgumentsPropertyName, StringComparison.Ordinal))
                            {
                                reader.Read();

                                JsonUtils.EnsureTokenType(ArgumentsPropertyName, reader.TokenType, JsonToken.StartArray);

                                try
                                {
                                    var paramTypes = binder.GetParameterTypes(target);
                                    arguments = BindArguments(reader, paramTypes);
                                }
                                catch (Exception ex)
                                {
                                    argumentBindingException = ExceptionDispatchInfo.Capture(ex);
                                }

                                finished = true;
                            }
                        }
                        break;
                    default:
                        break;
                }
            }
            while (reader.Read() && !finished);

            if (!finished)
            {
                throw new InvalidDataException($"Missing required property '{ArgumentsPropertyName}'.");
            }

            return new InvocationMessage(invocationId, target, argumentBindingException: argumentBindingException, arguments: arguments);
        }

        private object[] BindArguments(JsonReader reader, IReadOnlyList<Type> paramTypes)
        {
            var arguments = new object[paramTypes.Count];

            try
            {
                for (var i = 0; i < paramTypes.Count; i++)
                {
                    reader.Read();
                    var paramType = paramTypes[i];
                    arguments[i] = PayloadSerializer.Deserialize(reader, paramType);
                }

                return arguments;
            }
            catch (Exception ex)
            {
                throw new InvalidDataException("Error binding arguments. Make sure that the types of the provided values match the types of the hub method being invoked.", ex);
            }
        }

        private HubMessage ApplyHeaders(HubMessage message, Dictionary<string, string> headers)
        {
            if (headers != null && message is HubInvocationMessage invocationMessage)
            {
                invocationMessage.Headers = headers;
            }

            return message;
        }

        internal static JsonSerializerSettings CreateDefaultSerializerSettings()
        {
            return new JsonSerializerSettings { ContractResolver = new CamelCasePropertyNamesContractResolver() };
        }
    }
}
