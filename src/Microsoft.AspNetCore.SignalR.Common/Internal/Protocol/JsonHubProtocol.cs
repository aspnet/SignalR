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
                JArray argumentToken = null;
                ExceptionDispatchInfo argumentBindingException = null;
                Dictionary<string, string> headers = null;

                using (var reader = new JsonTextReader(textReader))
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
                                    else if (string.Equals(memberName, ResultPropertyName, StringComparison.Ordinal))
                                    {
                                        reader.Read();

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
                                    }
                                    else if (string.Equals(memberName, ItemPropertyName, StringComparison.Ordinal))
                                    {
                                        reader.Read();

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
                                    }
                                    else if (string.Equals(memberName, ArgumentsPropertyName, StringComparison.Ordinal))
                                    {
                                        reader.Read();

                                        JsonUtils.EnsureTokenType(ArgumentsPropertyName, reader.TokenType, JsonToken.StartArray);

                                        if (string.IsNullOrEmpty(target))
                                        {
                                            // We don't know the method name yet so just parse an array of generic JArray
                                            argumentToken = JArray.Load(reader);
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


                HubMessage message = null;

                switch (type)
                {
                    case HubProtocolConstants.InvocationMessageType:
                        message = BindInvocationMessage(invocationId, target, argumentBindingException, argumentToken, arguments, binder);
                        break;
                    case HubProtocolConstants.StreamInvocationMessageType:
                        message = BindStreamInvocationMessage(invocationId, target, argumentBindingException, argumentToken, arguments, binder);
                        break;
                    case HubProtocolConstants.StreamItemMessageType:
                        message = BindStreamItemMessage(invocationId, item, itemToken, binder);
                        break;
                    case HubProtocolConstants.CompletionMessageType:
                        message = BindCompletionMessage(invocationId, error, result, resultToken, binder);
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

        private HubMessage BindCompletionMessage(string invocationId, string error, object result, JToken resultToken, IInvocationBinder binder)
        {
            if (string.IsNullOrEmpty(invocationId))
            {
                throw new InvalidDataException($"Missing required property '{InvocationIdPropertyName}'.");
            }

            bool hasResult = result != null || resultToken != null;

            // Finished means we have a result property
            if (error != null && hasResult)
            {
                throw new InvalidDataException("The 'error' and 'result' properties are mutually exclusive.");
            }

            if (hasResult)
            {
                if (resultToken != null)
                {
                    var returnType = binder.GetReturnType(invocationId);
                    result = resultToken.ToObject(returnType, PayloadSerializer);
                }

                return new CompletionMessage(invocationId, error, result, hasResult: true);
            }

            return new CompletionMessage(invocationId, error, result: null, hasResult: false);
        }

        private HubMessage BindStreamItemMessage(string invocationId, object item, JToken itemToken, IInvocationBinder binder)
        {
            if (string.IsNullOrEmpty(invocationId))
            {
                throw new InvalidDataException($"Missing required property '{InvocationIdPropertyName}'.");
            }

            if (itemToken != null)
            {
                var returnType = binder.GetReturnType(invocationId);
                item = itemToken.ToObject(returnType, PayloadSerializer);
            }

            return new StreamItemMessage(invocationId, item);
        }

        private HubMessage BindStreamInvocationMessage(string invocationId, string target, ExceptionDispatchInfo argumentBindingException, JArray argumentToken, object[] arguments, IInvocationBinder binder)
        {
            if (string.IsNullOrEmpty(invocationId))
            {
                throw new InvalidDataException($"Missing required property '{InvocationIdPropertyName}'.");
            }

            if (string.IsNullOrEmpty(target))
            {
                throw new InvalidDataException($"Missing required property '{TargetPropertyName}'.");
            }

            if (argumentToken != null)
            {
                // We didn't bind arguments yet since those appeared in the JSON object before the target, lets bind now
                try
                {
                    var paramTypes = binder.GetParameterTypes(target);
                    arguments = BindArguments(argumentToken, paramTypes);
                }
                catch (Exception ex)
                {
                    argumentBindingException = ExceptionDispatchInfo.Capture(ex);
                }
            }

            return new StreamInvocationMessage(invocationId, target, argumentBindingException: argumentBindingException, arguments: arguments);
        }

        private HubMessage BindInvocationMessage(string invocationId, string target, ExceptionDispatchInfo argumentBindingException, JArray argumentToken, object[] arguments, IInvocationBinder binder)
        {
            if (string.IsNullOrEmpty(target))
            {
                throw new InvalidDataException($"Missing required property '{TargetPropertyName}'.");
            }

            if (argumentToken != null)
            {
                // We didn't bind arguments yet since those appeared in the JSON object before the target, lets bind now
                try
                {
                    var paramTypes = binder.GetParameterTypes(target);
                    arguments = BindArguments(argumentToken, paramTypes);
                }
                catch (Exception ex)
                {
                    argumentBindingException = ExceptionDispatchInfo.Capture(ex);
                }
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
