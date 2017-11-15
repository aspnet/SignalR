// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.ExceptionServices;
using Microsoft.AspNetCore.SignalR.Internal.Formatters;
using MsgPack;
using MsgPack.Serialization;

namespace Microsoft.AspNetCore.SignalR.Internal.Protocol
{
    public class MessagePackHubProtocol : IHubProtocol
    {
        private const int ErrorResult = 1;
        private const int VoidResult = 2;
        private const int NonVoidResult = 3;

        private readonly SerializationContext _serializationContext;

        public string Name => "messagepack";

        public ProtocolType Type => ProtocolType.Binary;

        public MessagePackHubProtocol()
            : this(CreateDefaultSerializationContext())
        { }

        public MessagePackHubProtocol(SerializationContext serializationContext)
        {
            _serializationContext = serializationContext;
        }

        public bool TryParseMessages(ReadOnlyMemory<byte> input, IInvocationBinder binder, out IList<HubMessage> messages)
        {
            messages = new List<HubMessage>();

            while (BinaryMessageParser.TryParseMessage(ref input, out var payload))
            {
                using (var memoryStream = new MemoryStream(payload.ToArray()))
                {
                    messages.Add(ParseMessage(memoryStream, binder));
                }
            }

            return messages.Count > 0;
        }

        private static HubMessage ParseMessage(Stream input, IInvocationBinder binder)
        {
            using (var unpacker = Unpacker.Create(input))
            {
                _ = ReadArrayLength(unpacker, "elementCount");
                var messageType = ReadInt32(unpacker, "messageType");

                switch (messageType)
                {
                    case HubProtocolConstants.InvocationMessageType:
                        return CreateInvocationMessage(unpacker, binder);
                    case HubProtocolConstants.StreamInvocationMessageType:
                        return CreateStreamInvocationMessage(unpacker, binder);
                    case HubProtocolConstants.StreamItemMessageType:
                        return CreateStreamItemMessage(unpacker, binder);
                    case HubProtocolConstants.CompletionMessageType:
                        return CreateCompletionMessage(unpacker, binder);
                    case HubProtocolConstants.CancelInvocationMessageType:
                        return CreateCancelInvocationMessage(unpacker);
                    case HubProtocolConstants.PingMessageType:
                        return CreatePingMessage(unpacker);
                    case HubProtocolConstants.PongMessageType:
                        return CreatePongMessage(unpacker);
                    default:
                        throw new FormatException($"Invalid message type: {messageType}.");
                }
            }
        }

        private static InvocationMessage CreateInvocationMessage(Unpacker unpacker, IInvocationBinder binder)
        {
            var invocationId = ReadInvocationId(unpacker);
            var nonBlocking = ReadBoolean(unpacker, "nonBlocking");
            var target = ReadString(unpacker, "target");
            var parameterTypes = binder.GetParameterTypes(target);

            try
            {
                var arguments = BindArguments(unpacker, parameterTypes);
                return new InvocationMessage(invocationId, nonBlocking, target, argumentBindingException: null, arguments: arguments);
            }
            catch (Exception ex)
            {
                return new InvocationMessage(invocationId, nonBlocking, target, ExceptionDispatchInfo.Capture(ex));
            }
        }

        private static StreamInvocationMessage CreateStreamInvocationMessage(Unpacker unpacker, IInvocationBinder binder)
        {
            var invocationId = ReadInvocationId(unpacker);
            var target = ReadString(unpacker, "target");
            var parameterTypes = binder.GetParameterTypes(target);
            try
            {
                var arguments = BindArguments(unpacker, parameterTypes);
                return new StreamInvocationMessage(invocationId, target, argumentBindingException: null, arguments: arguments);
            }
            catch (Exception ex)
            {
                return new StreamInvocationMessage(invocationId, target, ExceptionDispatchInfo.Capture(ex));
            }
        }

        private static object[] BindArguments(Unpacker unpacker, Type[] parameterTypes)
        {
            var argumentCount = ReadArrayLength(unpacker, "arguments");

            if (parameterTypes.Length != argumentCount)
            {
                throw new FormatException(
                    $"Invocation provides {argumentCount} argument(s) but target expects {parameterTypes.Length}.");
            }

            try
            {
                var arguments = new object[argumentCount];
                for (var i = 0; i < argumentCount; i++)
                {
                    arguments[i] = DeserializeObject(unpacker, parameterTypes[i], "argument");
                }

                return arguments;
            }
            catch (Exception ex)
            {
                throw new FormatException("Error binding arguments. Make sure that the types of the provided values match the types of the hub method being invoked.", ex);
            }
        }

        private static StreamItemMessage CreateStreamItemMessage(Unpacker unpacker, IInvocationBinder binder)
        {
            var invocationId = ReadInvocationId(unpacker);
            var itemType = binder.GetReturnType(invocationId);
            var value = DeserializeObject(unpacker, itemType, "item");
            return new StreamItemMessage(invocationId, value);
        }

        private static CompletionMessage CreateCompletionMessage(Unpacker unpacker, IInvocationBinder binder)
        {
            var invocationId = ReadInvocationId(unpacker);
            var resultKind = ReadInt32(unpacker, "resultKind");

            string error = null;
            object result = null;
            var hasResult = false;

            switch(resultKind)
            {
                case ErrorResult:
                    error = ReadString(unpacker, "error");
                    break;
                case NonVoidResult:
                    var itemType = binder.GetReturnType(invocationId);
                    result = DeserializeObject(unpacker, itemType, "argument");
                    hasResult = true;
                    break;
                case VoidResult:
                    hasResult = false;
                    break;
                default:
                    throw new FormatException("Invalid invocation result kind.");
            }

            return new CompletionMessage(invocationId, error, result, hasResult);
        }

        private static CancelInvocationMessage CreateCancelInvocationMessage(Unpacker unpacker)
        {
            var invocationId = ReadInvocationId(unpacker);
            return new CancelInvocationMessage(invocationId);
        }

        private static HubMessage CreatePongMessage(Unpacker unpacker)
        {
            var payload = ReadString(unpacker, "payload");
            return new PongMessage(payload);
        }

        private static HubMessage CreatePingMessage(Unpacker unpacker)
        {
            var payload = ReadString(unpacker, "payload");
            return new PingMessage(payload);
        }

        public void WriteMessage(HubMessage message, Stream output)
        {
            using (var memoryStream = new MemoryStream())
            {
                WriteMessageCore(message, memoryStream);
                BinaryMessageFormatter.WriteMessage(new ReadOnlySpan<byte>(memoryStream.ToArray()), output);
            }
        }

        private void WriteMessageCore(HubMessage message, Stream output)
        {
            // PackerCompatibilityOptions.None prevents from serializing byte[] as strings
            // and allows extended objects
            var packer = Packer.Create(output, PackerCompatibilityOptions.None);
            switch (message)
            {
                case InvocationMessage invocationMessage:
                    WriteInvocationMessage(invocationMessage, packer);
                    break;
                case StreamInvocationMessage streamInvocationMessage:
                    WriteStreamInvocationMessage(streamInvocationMessage, packer);
                    break;
                case StreamItemMessage streamItemMessage:
                    WriteStreamingItemMessage(streamItemMessage, packer);
                    break;
                case CompletionMessage completionMessage:
                    WriteCompletionMessage(completionMessage, packer);
                    break;
                case CancelInvocationMessage cancelInvocationMessage:
                    WriteCancelInvocationMessage(cancelInvocationMessage, packer);
                    break;
                case PingMessage pingMessage:
                    WritePingMessage(pingMessage, packer);
                    break;
                case PongMessage pingMessage:
                    WritePongMessage(pingMessage, packer);
                    break;
                default:
                    throw new FormatException($"Unexpected message type: {message.GetType().Name}");
            }
        }

        private void WriteInvocationMessage(InvocationMessage invocationMessage, Packer packer)
        {
            packer.PackArrayHeader(5);
            packer.Pack(HubProtocolConstants.InvocationMessageType);
            packer.PackString(invocationMessage.InvocationId);
            packer.Pack(invocationMessage.NonBlocking);
            packer.PackString(invocationMessage.Target);
            packer.PackObject(invocationMessage.Arguments, _serializationContext);
        }

        private void WriteStreamInvocationMessage(StreamInvocationMessage streamInvocationMessage, Packer packer)
        {
            packer.PackArrayHeader(4);
            packer.Pack(HubProtocolConstants.StreamInvocationMessageType);
            packer.PackString(streamInvocationMessage.InvocationId);
            packer.PackString(streamInvocationMessage.Target);
            packer.PackObject(streamInvocationMessage.Arguments, _serializationContext);
        }

        private void WriteStreamingItemMessage(StreamItemMessage streamItemMessage, Packer packer)
        {
            packer.PackArrayHeader(3);
            packer.Pack(HubProtocolConstants.StreamItemMessageType);
            packer.PackString(streamItemMessage.InvocationId);
            packer.PackObject(streamItemMessage.Item, _serializationContext);
        }

        private void WriteCompletionMessage(CompletionMessage completionMessage, Packer packer)
        {
            var resultKind =
                completionMessage.Error != null ? ErrorResult :
                completionMessage.HasResult ? NonVoidResult :
                VoidResult;

            packer.PackArrayHeader(3 + (resultKind != VoidResult ? 1 : 0));

            packer.Pack(HubProtocolConstants.CompletionMessageType);
            packer.PackString(completionMessage.InvocationId);
            packer.Pack(resultKind);
            switch (resultKind)
            {
                case ErrorResult:
                    packer.PackString(completionMessage.Error);
                    break;
                case NonVoidResult:
                    packer.PackObject(completionMessage.Result, _serializationContext);
                    break;
            }
        }

        private void WriteCancelInvocationMessage(CancelInvocationMessage cancelInvocationMessage, Packer packer)
        {
            packer.PackArrayHeader(2);
            packer.Pack(HubProtocolConstants.CancelInvocationMessageType);
            packer.PackString(cancelInvocationMessage.InvocationId);
        }

        private void WritePongMessage(PongMessage pingMessage, Packer packer)
        {
            packer.PackArrayHeader(2);
            packer.Pack(HubProtocolConstants.PongMessageType);
            packer.PackString(pingMessage.Payload);
        }

        private void WritePingMessage(PingMessage pingMessage, Packer packer)
        {
            packer.PackArrayHeader(2);
            packer.Pack(HubProtocolConstants.PingMessageType);
            packer.PackString(pingMessage.Payload);
        }

        private static string ReadInvocationId(Unpacker unpacker)
        {
            return ReadString(unpacker, "invocationId");
        }

        private static int ReadInt32(Unpacker unpacker, string field)
        {
            Exception msgPackException = null;
            try
            {
                if (unpacker.ReadInt32(out var value))
                {
                    return value;
                }
            }
            catch (Exception e)
            {
                msgPackException = e;
            }

            throw new FormatException($"Reading '{field}' as Int32 failed.", msgPackException);
        }

        private static string ReadString(Unpacker unpacker, string field)
        {
            Exception msgPackException = null;
            try
            {
                if (unpacker.ReadString(out var value))
                {
                    return value;
                }
            }
            catch (Exception e)
            {
                msgPackException = e;
            }

            throw new FormatException($"Reading '{field}' as String failed.", msgPackException);
        }

        private static bool ReadBoolean(Unpacker unpacker, string field)
        {
            Exception msgPackException = null;
            try
            {
                if (unpacker.ReadBoolean(out var value))
                {
                    return value;
                }
            }
            catch (Exception e)
            {
                msgPackException = e;
            }

            throw new FormatException($"Reading '{field}' as Boolean failed.", msgPackException);
        }

        private static long ReadArrayLength(Unpacker unpacker, string field)
        {
            Exception msgPackException = null;
            try
            {
                if (unpacker.ReadArrayLength(out var value))
                {
                    return value;
                }
            }
            catch (Exception e)
            {
                msgPackException = e;
            }

            throw new FormatException($"Reading array length for '{field}' failed.", msgPackException);
        }

        private static object DeserializeObject(Unpacker unpacker, Type type, string field)
        {
            Exception msgPackException = null;
            try
            {
                if (unpacker.Read())
                {
                    var serializer = MessagePackSerializer.Get(type);
                    return serializer.UnpackFrom(unpacker);
                }
            }
            catch (Exception ex)
            {
                msgPackException = ex;
            }

            throw new FormatException($"Deserializing object of the `{type.Name}` type for '{field}' failed.", msgPackException);
        }

        public static SerializationContext CreateDefaultSerializationContext()
        {
            // serializes objects (here: arguments and results) as maps so that property names are preserved
            var serializationContext = new SerializationContext { SerializationMethod = SerializationMethod.Map };
            // allows for serializing objects that cannot be deserialized due to the lack of the default ctor etc.
            serializationContext.CompatibilityOptions.AllowAsymmetricSerializer = true;
            return serializationContext;
        }
    }
}
