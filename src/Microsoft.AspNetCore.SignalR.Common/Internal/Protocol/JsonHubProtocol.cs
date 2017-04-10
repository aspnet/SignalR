// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.IO;
using Microsoft.AspNetCore.Sockets;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.AspNetCore.SignalR.Internal.Protocol
{
    public class JsonHubProtocol : IHubProtocol
    {
        private const string PayloadPropertyName = "payload";
        private const string InvocationIdPropertyName = "invocationId";
        private const string TypePropertyName = "type";
        private const string ErrorPropertyName = "error";
        private const string TargetPropertyName = "target";
        private const string ArgumentsPropertyName = "arguments";

        private const int InvocationMessageType = 1;
        private const int ResultMessageType = 2;
        private const int CompletionMessageType = 3;

        private JsonSerializer _serializer = new JsonSerializer();

        public MessageType MessageType => MessageType.Text;

        public JsonHubProtocol()
        {
        }

        public bool TryParseMessage(ReadOnlySpan<byte> input, IInvocationBinder binder, out HubMessage message)
        {
            // TODO: Need a span-native JSON parser!
            using (var memoryStream = new MemoryStream(input.ToArray()))
            {
                message = ParseMessage(memoryStream, binder);
            }
            return true;
        }

        public bool TryWriteMessage(HubMessage message, IOutput output)
        {
            // TODO: Need IOutput-compatible JSON serializer!
            using (var memoryStream = new MemoryStream())
            {
                WriteMessage(message, memoryStream);
                memoryStream.FlushAsync();

                return output.TryWrite(memoryStream.ToArray());
            }
        }

        private HubMessage ParseMessage(Stream input, IInvocationBinder binder)
        {
            var reader = new JsonTextReader(new StreamReader(input));

            // PERF: Could probably use the JsonTextReader directly for better perf and fewer allocations
            var json = _serializer.Deserialize<JObject>(reader);
            if (json == null)
            {
                return null;
            }

            // Determine the type of the message
            var type = json.Value<int>(TypePropertyName);
            switch (type)
            {
                case InvocationMessageType:
                    // Invocation
                    return BindInvocationMessage(json, binder);
                case ResultMessageType:
                    // Result
                    return BindResultMessage(json, binder);
                case CompletionMessageType:
                    // Completion
                    return BindCompletionMessage(json);
                default:
                    throw new FormatException($"Unknown message type: {type}");
            }
        }

        private void WriteMessage(HubMessage message, Stream stream)
        {
            using (var writer = new JsonTextWriter(new StreamWriter(stream)))
            {
                switch (message)
                {
                    case InvocationMessage m:
                        WriteInvocationMessage(m, writer);
                        break;
                    case ResultMessage m:
                        WriteResultMessage(m, writer);
                        break;
                    case CompletionMessage m:
                        WriteCompletionMessage(m, writer);
                        break;
                    default:
                        throw new InvalidOperationException($"Unsupported message type: {message.GetType().FullName}");
                }
            }
        }

        private static void WriteCompletionMessage(CompletionMessage message, JsonTextWriter writer)
        {
            writer.WriteStartObject();
            WriteHubMessageCommon(message, writer, CompletionMessageType);
            writer.WriteEndObject();
        }

        private static void WriteResultMessage(ResultMessage message, JsonTextWriter writer)
        {
            writer.WriteStartObject();
            WriteHubMessageCommon(message, writer, ResultMessageType);
            if (string.IsNullOrEmpty(message.Error))
            {
                writer.WritePropertyName(PayloadPropertyName);
                writer.WriteValue(message.Payload);
            }
            else
            {
                writer.WritePropertyName(ErrorPropertyName);
                writer.WriteValue(message.Error);
            }
            writer.WriteEndObject();
        }

        private static void WriteInvocationMessage(InvocationMessage message, JsonTextWriter writer)
        {
            writer.WriteStartObject();
            WriteHubMessageCommon(message, writer, InvocationMessageType);
            writer.WritePropertyName(TargetPropertyName);
            writer.WriteValue(message.Target);

            writer.WritePropertyName(ArgumentsPropertyName);
            writer.WriteStartArray();
            foreach (var argument in message.Arguments)
            {
                writer.WriteValue(argument);
            }
            writer.WriteEndArray();

            writer.WriteEndObject();
        }

        private static void WriteHubMessageCommon(HubMessage message, JsonTextWriter writer, int type)
        {
            writer.WritePropertyName(InvocationIdPropertyName);
            writer.WriteValue(message.InvocationId);
            writer.WritePropertyName(TypePropertyName);
            writer.WriteValue(type);
        }

        private InvocationMessage BindInvocationMessage(JObject json, IInvocationBinder binder)
        {
            var invocationId = json.Value<string>(InvocationIdPropertyName);
            var target = json.Value<string>(TargetPropertyName);

            var paramTypes = binder.GetParameterTypes(target);
            var arguments = new object[paramTypes.Length];

            var args = json.Value<JArray>(ArgumentsPropertyName);
            for (var i = 0; i < paramTypes.Length; i++)
            {
                var paramType = paramTypes[i];

                // TODO(anurse): We can add some DI magic here to allow users to provide their own serialization
                // Related Bug: https://github.com/aspnet/SignalR/issues/261
                arguments[i] = args[i].ToObject(paramType, _serializer);
            }

            return new InvocationMessage(invocationId, target, arguments);
        }

        private ResultMessage BindResultMessage(JObject json, IInvocationBinder binder)
        {
            var invocationId = json.Value<string>(InvocationIdPropertyName);
            var error = json.Value<string>(ErrorPropertyName);
            var payload = json.Value<JToken>(PayloadPropertyName);

            var returnType = binder.GetReturnType(invocationId);
            return new ResultMessage(invocationId, error, payload?.ToObject(returnType, _serializer));
        }

        private CompletionMessage BindCompletionMessage(JObject json)
        {
            var invocationId = json.Value<string>(InvocationIdPropertyName);
            return new CompletionMessage(invocationId);
        }
    }
}
