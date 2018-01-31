// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.ExceptionServices;
using Microsoft.AspNetCore.SignalR.Internal.Formatters;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Microsoft.AspNetCore.SignalR.Internal.Protocol
{
    public class JsonHubProtocol : IHubProtocol
    {
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

        public ProtocolType Type => ProtocolType.Text;

        public bool TryParseMessages(ReadOnlySpan<byte> input, IInvocationBinder binder, out IList<HubMessage> messages)
        {
            messages = new List<HubMessage>();

            while (TextMessageParser.TryParseMessage(ref input, out var payload))
            {
                // TODO: Need a span-native JSON parser!
                using (var memoryStream = new MemoryStream(payload.ToArray()))
                {
                    messages.Add(ParseMessage(memoryStream, binder));
                }
            }

            return messages.Count > 0;
        }

        public void WriteMessage(HubMessage message, Stream output)
        {
            using (var memoryStream = new MemoryStream())
            {
                WriteMessageCore(message, memoryStream);
                memoryStream.Flush();

                TextMessageFormatter.WriteMessage(memoryStream.ToArray(), output);
            }
        }

        private HubMessage ParseMessage(Stream input, IInvocationBinder binder)
        {
            using (var reader = new JsonTextReader(new StreamReader(input)))
            {
                try
                {
                    // PERF: Could probably use the JsonTextReader directly for better perf and fewer allocations
                    var token = JToken.ReadFrom(reader);

                    if (token == null || token.Type != JTokenType.Object)
                    {
                        throw new InvalidDataException($"Unexpected JSON Token Type '{token?.Type}'. Expected a JSON Object.");
                    }

                    var json = (JObject)token;

                    // Determine the type of the message
                    var type = JsonUtils.GetRequiredProperty<int>(json, TypePropertyName, JTokenType.Integer);

                    HubMessage m;
                    switch (type)
                    {
                        case HubProtocolConstants.InvocationMessageType:
                            m = BindInvocationMessage(json, binder);
                            break;
                        case HubProtocolConstants.StreamInvocationMessageType:
                            m = BindStreamInvocationMessage(json, binder);
                            break;
                        case HubProtocolConstants.StreamItemMessageType:
                            m = BindStreamItemMessage(json, binder);
                            break;
                        case HubProtocolConstants.CompletionMessageType:
                            m = BindCompletionMessage(json, binder);
                            break;
                        case HubProtocolConstants.CancelInvocationMessageType:
                            m = BindCancelInvocationMessage(json);
                            break;
                        case HubProtocolConstants.PingMessageType:
                            m = new PingMessage();
                            break;
                        default:
                            throw new InvalidDataException($"Unknown message type: {type}");
                    }

                    // Read headers
                    ReadHeaders(json, m.Headers);
                    return m;
                }
                catch (JsonReaderException jrex)
                {
                    throw new InvalidDataException("Error reading JSON.", jrex);
                }
            }
        }

        private void ReadHeaders(JObject json, IDictionary<string, string> headers)
        {
            var headersProp = json[HeadersPropertyName];
            if (headersProp != null)
            {
                if (headersProp.Type != JTokenType.Object)
                {
                    throw new InvalidDataException($"Expected '{HeadersPropertyName}' to be of type {JTokenType.Object}.");
                }
                var headersObj = headersProp.Value<JObject>();
                foreach (var prop in headersObj)
                {
                    if (prop.Value.Type != JTokenType.String)
                    {
                        throw new InvalidDataException($"Expected header '{prop.Key}' to be of type {JTokenType.String}.");
                    }
                    headers[prop.Key] = prop.Value.Value<string>();
                }
            }
        }

        private void WriteMessageCore(HubMessage message, Stream stream)
        {
            using (var writer = new JsonTextWriter(new StreamWriter(stream)))
            {
                writer.WriteStartObject();
                WriteHeaders(writer, message.Headers);
                switch (message)
                {
                    case InvocationMessage m:
                        WriteMessageType(writer, HubProtocolConstants.InvocationMessageType);
                        WriteInvocationMessage(m, writer);
                        break;
                    case StreamInvocationMessage m:
                        WriteMessageType(writer, HubProtocolConstants.StreamInvocationMessageType);
                        WriteStreamInvocationMessage(m, writer);
                        break;
                    case StreamItemMessage m:
                        WriteMessageType(writer, HubProtocolConstants.StreamItemMessageType);
                        WriteStreamItemMessage(m, writer);
                        break;
                    case CompletionMessage m:
                        WriteMessageType(writer, HubProtocolConstants.CompletionMessageType);
                        WriteCompletionMessage(m, writer);
                        break;
                    case CancelInvocationMessage m:
                        WriteMessageType(writer, HubProtocolConstants.CancelInvocationMessageType);
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

        private void WriteHeaders(JsonTextWriter writer, IDictionary<string, string> headers)
        {
            if (headers.Count > 0)
            {
                writer.WritePropertyName(HeadersPropertyName);
                writer.WriteStartObject();
                foreach (var value in headers)
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

        private InvocationMessage BindInvocationMessage(JObject json, IInvocationBinder binder)
        {
            var invocationId = JsonUtils.GetOptionalProperty<string>(json, InvocationIdPropertyName, JTokenType.String);
            var target = JsonUtils.GetRequiredProperty<string>(json, TargetPropertyName, JTokenType.String);

            var args = JsonUtils.GetRequiredProperty<JArray>(json, ArgumentsPropertyName, JTokenType.Array);

            var paramTypes = binder.GetParameterTypes(target);

            try
            {
                var arguments = BindArguments(args, paramTypes);
                return new InvocationMessage(invocationId, target, argumentBindingException: null, arguments: arguments);
            }
            catch (Exception ex)
            {
                return new InvocationMessage(invocationId, target, ExceptionDispatchInfo.Capture(ex));
            }
        }

        private StreamInvocationMessage BindStreamInvocationMessage(JObject json, IInvocationBinder binder)
        {
            var invocationId = JsonUtils.GetRequiredProperty<string>(json, InvocationIdPropertyName, JTokenType.String);
            var target = JsonUtils.GetRequiredProperty<string>(json, TargetPropertyName, JTokenType.String);

            var args = JsonUtils.GetRequiredProperty<JArray>(json, ArgumentsPropertyName, JTokenType.Array);

            var paramTypes = binder.GetParameterTypes(target);

            try
            {
                var arguments = BindArguments(args, paramTypes);
                return new StreamInvocationMessage(invocationId, target, argumentBindingException: null, arguments: arguments);
            }
            catch (Exception ex)
            {
                return new StreamInvocationMessage(invocationId, target, ExceptionDispatchInfo.Capture(ex));
            }
        }

        private object[] BindArguments(JArray args, Type[] paramTypes)
        {
            var arguments = new object[args.Count];
            if (paramTypes.Length != arguments.Length)
            {
                throw new InvalidDataException($"Invocation provides {arguments.Length} argument(s) but target expects {paramTypes.Length}.");
            }

            try
            {
                for (var i = 0; i < paramTypes.Length; i++)
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

        private StreamItemMessage BindStreamItemMessage(JObject json, IInvocationBinder binder)
        {
            var invocationId = JsonUtils.GetRequiredProperty<string>(json, InvocationIdPropertyName, JTokenType.String);
            var result = JsonUtils.GetRequiredProperty<JToken>(json, ItemPropertyName);

            var returnType = binder.GetReturnType(invocationId);
            return new StreamItemMessage(invocationId, result?.ToObject(returnType, PayloadSerializer));
        }

        private CompletionMessage BindCompletionMessage(JObject json, IInvocationBinder binder)
        {
            var invocationId = JsonUtils.GetRequiredProperty<string>(json, InvocationIdPropertyName, JTokenType.String);
            var error = JsonUtils.GetOptionalProperty<string>(json, ErrorPropertyName, JTokenType.String);
            var resultProp = json.Property(ResultPropertyName);

            if (error != null && resultProp != null)
            {
                throw new InvalidDataException("The 'error' and 'result' properties are mutually exclusive.");
            }

            if (resultProp == null)
            {
                return new CompletionMessage(invocationId, error, result: null, hasResult: false);
            }

            var returnType = binder.GetReturnType(invocationId);
            var payload = resultProp.Value?.ToObject(returnType, PayloadSerializer);
            return new CompletionMessage(invocationId, error, result: payload, hasResult: true);
        }

        private CancelInvocationMessage BindCancelInvocationMessage(JObject json)
        {
            var invocationId = JsonUtils.GetRequiredProperty<string>(json, InvocationIdPropertyName, JTokenType.String);
            return new CancelInvocationMessage(invocationId);
        }

        internal static JsonSerializerSettings CreateDefaultSerializerSettings()
        {
            return new JsonSerializerSettings { ContractResolver = new CamelCasePropertyNamesContractResolver() };
        }
    }
}
