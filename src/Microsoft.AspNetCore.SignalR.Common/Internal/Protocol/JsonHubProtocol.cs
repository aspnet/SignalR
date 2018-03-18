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
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Microsoft.AspNetCore.SignalR.Internal.Protocol
{
    public class JsonHubProtocol : IHubProtocol
    {
        private static readonly UTF8Encoding _utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

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
                var textReader = new Utf8BufferTextReader(payload);
                messages.Add(ParseMessage(textReader, binder));
            }

            return messages.Count > 0;
        }

        public void WriteMessage(HubMessage message, Stream output)
        {
            WriteMessageCore(message, output);
            TextMessageFormatter.WriteRecordSeparator(output);
        }

        private HubMessage ParseMessage(TextReader textReader, IInvocationBinder binder)
        {
            try
            {
                // We parse using the JsonTextReader directly but this has a problem. Some of our properties are dependent on other properties
                // and since reading the json might be unordered, we need to store the parsed content as JToken to re-parse when true types are known.
                // if we're lucky and the state we need to directly parse is available, then we'll use it.

                int? type = null;
                string invocationId = null;
                string target = null;
                string error = null;
                object item = null;
                JToken itemToken = null;
                object result = null;
                JToken resultToken = null;
                object[] arguments = null;
                JArray argumentsToken = null;
                ExceptionDispatchInfo argumentBindingException = null;
                Dictionary<string, string> headers = null;
                var completed = false;

                using (var reader = new JsonTextReader(textReader))
                {
                    reader.ArrayPool = JsonArrayPool<char>.Shared;

                    if (!reader.Read())
                    {
                        throw new JsonReaderException("Error reading JSON.");
                    }

                    // We're always parsing a JSON object
                    if (reader.TokenType != JsonToken.StartObject)
                    {
                        throw new InvalidDataException($"Unexpected JSON Token Type '{reader.TokenType}'. Expected a JSON Object.");
                    }

                    do
                    {
                        switch (reader.TokenType)
                        {
                            case JsonToken.PropertyName:
                                string memberName = reader.Value.ToString();

                                switch (memberName)
                                {
                                    case TypePropertyName:
                                        var messageType = reader.ReadAsInt32();

                                        if (messageType == null)
                                        {
                                            throw new InvalidDataException($"Missing required property '{TypePropertyName}'.");
                                        }

                                        type = messageType.Value;
                                        break;
                                    case InvocationIdPropertyName:
                                        invocationId = reader.ReadAsString();
                                        break;
                                    case TargetPropertyName:
                                        target = reader.ReadAsString();
                                        break;
                                    case ErrorPropertyName:
                                        error = reader.ReadAsString();
                                        break;
                                    case ResultPropertyName:
                                        if (!reader.Read())
                                        {
                                            throw new JsonReaderException("Unexpected end when reading JSON");
                                        }

                                        if (string.IsNullOrEmpty(invocationId))
                                        {
                                            // If we don't have an invocation id then we need to store it as a JToken so we can parse it later
                                            resultToken = JToken.Load(reader);
                                        }
                                        else
                                        {
                                            // If we have an invocation id already we can parse the end result
                                            var returnType = binder.GetReturnType(invocationId);
                                            result = PayloadSerializer.Deserialize(reader);
                                        }
                                        break;
                                    case ItemPropertyName:
                                        if (!reader.Read())
                                        {
                                            throw new JsonReaderException("Unexpected end when reading JSON");
                                        }

                                        if (string.IsNullOrEmpty(invocationId))
                                        {
                                            // If we don't have an invocation id then we need to store it as a JToken so we can parse it later
                                            itemToken = JToken.Load(reader);
                                        }
                                        else
                                        {
                                            var returnType = binder.GetReturnType(invocationId);
                                            item = PayloadSerializer.Deserialize(reader);
                                        }
                                        break;
                                    case ArgumentsPropertyName:
                                        if (!reader.Read())
                                        {
                                            throw new JsonReaderException("Unexpected end when reading JSON");
                                        }

                                        JsonUtils.EnsureTokenType(ArgumentsPropertyName, reader.TokenType, JsonToken.StartArray);

                                        if (string.IsNullOrEmpty(target))
                                        {
                                            // We don't know the method name yet so just parse an array of generic JArray
                                            argumentsToken = JArray.Load(reader);
                                        }
                                        else
                                        {
                                            try
                                            {
                                                var paramTypes = binder.GetParameterTypes(target);
                                                arguments = BindArguments(reader, paramTypes);
                                            }
                                            catch (Exception ex)
                                            {
                                                argumentBindingException = ExceptionDispatchInfo.Capture(ex);
                                            }
                                        }
                                        break;
                                    case HeadersPropertyName:
                                        if (!reader.Read())
                                        {
                                            throw new JsonReaderException("Unexpected end when reading JSON");
                                        }
                                        headers = ReadHeaders(reader);
                                        break;
                                    default:
                                        // Skip read the property name
                                        if (!reader.Read())
                                        {
                                            throw new JsonReaderException("Unexpected end when reading JSON");
                                        }
                                        // Skip the value for this property
                                        reader.Skip();
                                        break;
                                }
                                break;
                            case JsonToken.EndObject:
                                completed = true;
                                break;
                            default:
                                break;
                        }
                    }
                    while (reader.Read() && !completed);
                }

                if (type == null)
                {
                    throw new InvalidDataException($"Missing required property '{TypePropertyName}'.");
                }

                HubMessage message = null;

                switch (type)
                {
                    case HubProtocolConstants.InvocationMessageType:
                        {
                            if (argumentsToken != null)
                            {
                                try
                                {
                                    var paramTypes = binder.GetParameterTypes(target);
                                    arguments = BindArguments(argumentsToken, paramTypes);
                                }
                                catch (Exception ex)
                                {
                                    argumentBindingException = ExceptionDispatchInfo.Capture(ex);
                                }
                            }

                            message = BindInvocationMessage(invocationId, target, argumentBindingException, arguments, binder);
                        }
                        break;
                    case HubProtocolConstants.StreamInvocationMessageType:
                        {
                            if (argumentsToken != null)
                            {
                                try
                                {
                                    var paramTypes = binder.GetParameterTypes(target);
                                    arguments = BindArguments(argumentsToken, paramTypes);
                                }
                                catch (Exception ex)
                                {
                                    argumentBindingException = ExceptionDispatchInfo.Capture(ex);
                                }
                            }

                            message = BindStreamInvocationMessage(invocationId, target, argumentBindingException, arguments, binder);
                        }
                        break;
                    case HubProtocolConstants.StreamItemMessageType:
                        if (itemToken != null)
                        {
                            var returnType = binder.GetReturnType(invocationId);
                            item = itemToken.ToObject(returnType, PayloadSerializer);
                        }

                        message = BindStreamItemMessage(invocationId, item, binder);
                        break;
                    case HubProtocolConstants.CompletionMessageType:
                        if (resultToken != null)
                        {
                            var returnType = binder.GetReturnType(invocationId);
                            result = resultToken.ToObject(returnType, PayloadSerializer);
                        }

                        message = BindCompletionMessage(invocationId, error, result, binder);
                        break;
                    case HubProtocolConstants.CancelInvocationMessageType:
                        message = BindCancelInvocationMessage(invocationId);
                        break;
                    case HubProtocolConstants.PingMessageType:
                        return PingMessage.Instance;
                    default:
                        throw new InvalidDataException($"Unknown message type: {type}");
                }

                return ApplyHeaders(message, headers);
            }
            catch (JsonReaderException jrex)
            {
                throw new InvalidDataException("Error reading JSON.", jrex);
            }
        }

        private Dictionary<string, string> ReadHeaders(JsonTextReader reader)
        {
            var headers = new Dictionary<string, string>();

            if (reader.TokenType != JsonToken.StartObject)
            {
                throw new InvalidDataException($"Expected '{HeadersPropertyName}' to be of type {JsonToken.StartObject}.");
            }

            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonToken.PropertyName:
                        string propertyName = reader.Value.ToString();

                        if (!reader.Read())
                        {
                            throw new JsonReaderException("Unexpected end when reading message headers");
                        }

                        JsonUtils.EnsureTokenType(propertyName, reader.TokenType, JsonToken.String);

                        headers[propertyName] = reader.Value.ToString();
                        break;
                    case JsonToken.Comment:
                        break;
                    case JsonToken.EndObject:
                        return headers;
                }
            }

            throw new JsonReaderException("Unexpected end when reading message headers");
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

        private HubMessage BindCancelInvocationMessage(string invocationId)
        {
            if (string.IsNullOrEmpty(invocationId))
            {
                throw new InvalidDataException($"Missing required property '{InvocationIdPropertyName}'.");
            }

            return new CancelInvocationMessage(invocationId);
        }

        private HubMessage BindCompletionMessage(string invocationId, string error, object result, IInvocationBinder binder)
        {
            if (string.IsNullOrEmpty(invocationId))
            {
                throw new InvalidDataException($"Missing required property '{InvocationIdPropertyName}'.");
            }

            if (error != null && result != null)
            {
                throw new InvalidDataException("The 'error' and 'result' properties are mutually exclusive.");
            }

            if (result != null)
            {
                return new CompletionMessage(invocationId, error, result, hasResult: true);
            }

            return new CompletionMessage(invocationId, error, result: null, hasResult: false);
        }

        private HubMessage BindStreamItemMessage(string invocationId, object item, IInvocationBinder binder)
        {
            if (string.IsNullOrEmpty(invocationId))
            {
                throw new InvalidDataException($"Missing required property '{InvocationIdPropertyName}'.");
            }

            return new StreamItemMessage(invocationId, item);
        }

        private HubMessage BindStreamInvocationMessage(string invocationId, string target, ExceptionDispatchInfo argumentBindingException, object[] arguments, IInvocationBinder binder)
        {
            if (string.IsNullOrEmpty(invocationId))
            {
                throw new InvalidDataException($"Missing required property '{InvocationIdPropertyName}'.");
            }

            if (string.IsNullOrEmpty(target))
            {
                throw new InvalidDataException($"Missing required property '{TargetPropertyName}'.");
            }

            return new StreamInvocationMessage(invocationId, target, argumentBindingException: argumentBindingException, arguments: arguments);
        }

        private HubMessage BindInvocationMessage(string invocationId, string target, ExceptionDispatchInfo argumentBindingException, object[] arguments, IInvocationBinder binder)
        {
            if (string.IsNullOrEmpty(target))
            {
                throw new InvalidDataException($"Missing required property '{TargetPropertyName}'.");
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

                    if (reader.TokenType == JsonToken.EndArray)
                    {
                        throw new InvalidDataException($"Invocation provides {i} argument(s) but target expects {paramTypes.Count}.");
                    }

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

        private object[] BindArguments(JArray args, IReadOnlyList<Type> paramTypes)
        {
            var arguments = new object[args.Count];
            if (paramTypes.Count != arguments.Length)
            {
                throw new InvalidDataException($"Invocation provides {arguments.Length} argument(s) but target expects {paramTypes.Count}.");
            }

            try
            {
                for (var i = 0; i < paramTypes.Count; i++)
                {
                    var paramType = paramTypes[i];
                    arguments[i] = args[i].ToObject(paramType, PayloadSerializer);
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
