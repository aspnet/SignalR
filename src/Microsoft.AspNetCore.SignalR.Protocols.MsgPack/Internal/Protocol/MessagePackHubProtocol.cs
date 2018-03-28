// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.SignalR.Internal.Formatters;
using Microsoft.Extensions.Options;
//using MsgPack;
//using MsgPack.Serialization;
using MessagePack;

namespace Microsoft.AspNetCore.SignalR.Internal.Protocol
{
    public class MessagePackHubProtocol : IHubProtocol
    {
        private const int ErrorResult = 1;
        private const int VoidResult = 2;
        private const int NonVoidResult = 3;

        public static readonly string ProtocolName = "messagepack";
        public static readonly int ProtocolVersion = 1;

        //public SerializationContext SerializationContext { get; }

        public string Name => ProtocolName;

        public int Version => ProtocolVersion;

        public TransferFormat TransferFormat => TransferFormat.Binary;

        public MessagePackHubProtocol()
            : this(Options.Create(new MessagePackHubProtocolOptions()))
        { }

        public MessagePackHubProtocol(IOptions<MessagePackHubProtocolOptions> options)
        {
            //SerializationContext = options.Value.SerializationContext;
        }

        public bool IsVersionSupported(int version)
        {
            return version == Version;
        }

        public bool TryParseMessages(ReadOnlyMemory<byte> input, IInvocationBinder binder, IList<HubMessage> messages)
        {
            while (BinaryMessageParser.TryParseMessage(ref input, out var payload))
            {
                var isArray = MemoryMarshal.TryGetArray(payload, out var arraySegment);
                // This will never be false unless we started using un-managed buffers
                Debug.Assert(isArray);
                var message = ParseMessage(arraySegment.Array, arraySegment.Offset, binder);
                if (message != null)
                {
                    messages.Add(message);
                }
            }

            return messages.Count > 0;
        }

        private static HubMessage ParseMessage(byte[] input, int startOffset, IInvocationBinder binder)
        {
            _ = MessagePack.MessagePackBinary.ReadArrayHeader(input);
            //using (var unpacker = Unpacker.Create(input, startOffset))
            {
                //_ = ReadArrayLength(unpacker, "elementCount");

                var messageType = MessagePackBinary.ReadInt32(input);
                //var messageType = ReadInt32(unpacker, "messageType");

                switch (messageType)
                {
                    case HubProtocolConstants.InvocationMessageType:
                        return CreateInvocationMessage(input, binder);
                    case HubProtocolConstants.StreamInvocationMessageType:
                        return CreateStreamInvocationMessage(input, binder);
                    case HubProtocolConstants.StreamItemMessageType:
                        return CreateStreamItemMessage(input, binder);
                    case HubProtocolConstants.CompletionMessageType:
                        return CreateCompletionMessage(input, binder);
                    case HubProtocolConstants.CancelInvocationMessageType:
                        return CreateCancelInvocationMessage(input);
                    case HubProtocolConstants.PingMessageType:
                        return PingMessage.Instance;
                    case HubProtocolConstants.CloseMessageType:
                        return CreateCloseMessage(unpacker);
                    default:
                        // Future protocol changes can add message types, old clients can ignore them
                        return null;
                }
            }
        }

        private static InvocationMessage CreateInvocationMessage(Stream unpacker, IInvocationBinder binder)
        {
            var headers = ReadHeaders(unpacker);
            var invocationId = ReadInvocationId(unpacker);

            // For MsgPack, we represent an empty invocation ID as an empty string,
            // so we need to normalize that to "null", which is what indicates a non-blocking invocation.
            if (string.IsNullOrEmpty(invocationId))
            {
                invocationId = null;
            }

            var target = ReadString(unpacker, "target");
            var parameterTypes = binder.GetParameterTypes(target);

            try
            {
                var arguments = BindArguments(unpacker, parameterTypes);
                return ApplyHeaders(headers, new InvocationMessage(invocationId, target, argumentBindingException: null, arguments: arguments));
            }
            catch (Exception ex)
            {
                return ApplyHeaders(headers, new InvocationMessage(invocationId, target, ExceptionDispatchInfo.Capture(ex)));
            }
        }

        private static StreamInvocationMessage CreateStreamInvocationMessage(Stream unpacker, IInvocationBinder binder)
        {
            var headers = ReadHeaders(unpacker);
            var invocationId = ReadInvocationId(unpacker);
            var target = ReadString(unpacker, "target");
            var parameterTypes = binder.GetParameterTypes(target);

            try
            {
                var arguments = BindArguments(unpacker, parameterTypes);
                return ApplyHeaders(headers, new StreamInvocationMessage(invocationId, target, argumentBindingException: null, arguments: arguments));
            }
            catch (Exception ex)
            {
                return ApplyHeaders(headers, new StreamInvocationMessage(invocationId, target, ExceptionDispatchInfo.Capture(ex)));
            }
        }

        private static StreamItemMessage CreateStreamItemMessage(Stream unpacker, IInvocationBinder binder)
        {
            var headers = ReadHeaders(unpacker);
            var invocationId = ReadInvocationId(unpacker);
            var itemType = binder.GetReturnType(invocationId);
            var value = DeserializeObject(unpacker, itemType, "item");
            return ApplyHeaders(headers, new StreamItemMessage(invocationId, value));
        }

        private static CompletionMessage CreateCompletionMessage(Stream unpacker, IInvocationBinder binder)
        {
            var headers = ReadHeaders(unpacker);
            var invocationId = ReadInvocationId(unpacker);
            var resultKind = ReadInt32(unpacker, "resultKind");

            string error = null;
            object result = null;
            var hasResult = false;

            switch (resultKind)
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

            return ApplyHeaders(headers, new CompletionMessage(invocationId, error, result, hasResult));
        }

        private static CancelInvocationMessage CreateCancelInvocationMessage(Stream unpacker)
        {
            var headers = ReadHeaders(unpacker);
            var invocationId = ReadInvocationId(unpacker);
            return ApplyHeaders(headers, new CancelInvocationMessage(invocationId));
        }

        private static CloseMessage CreateCloseMessage(Stream unpacker)
        {
            var error = ReadString(unpacker, "error");
            return new CloseMessage(error);
        }

        private static Dictionary<string, string> ReadHeaders(Stream unpacker)
        {
            var headerCount = ReadMapLength(unpacker, "headers");
            if (headerCount > 0)
            {
                // If headerCount is larger than int.MaxValue, things are going to go horribly wrong anyway :)
                var headers = new Dictionary<string, string>((int)headerCount);

                for (var i = 0; i < headerCount; i++)
                {
                    var key = ReadString(unpacker, $"headers[{i}].Key");
                    var value = ReadString(unpacker, $"headers[{i}].Value");
                    headers[key] = value;
                }
                return headers;
            }
            else
            {
                return null;
            }
        }

        private static object[] BindArguments(Stream unpacker, IReadOnlyList<Type> parameterTypes)
        {
            var argumentCount = ReadArrayLength(unpacker, "arguments");

            if (parameterTypes.Count != argumentCount)
            {
                throw new FormatException(
                    $"Invocation provides {argumentCount} argument(s) but target expects {parameterTypes.Count}.");
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

        private static T ApplyHeaders<T>(IDictionary<string, string> source, T destination) where T : HubInvocationMessage
        {
            if (source != null && source.Count > 0)
            {
                destination.Headers = source;
            }

            return destination;
        }

        public void WriteMessage(HubMessage message, Stream output)
        {
            // We're writing data into the memoryStream so that we can get the length prefix
            using (var memoryStream = new MemoryStream())
            {
                WriteMessageCore(message, memoryStream);
                if (memoryStream.TryGetBuffer(out var buffer))
                {
                    // Write the buffer directly
                    BinaryMessageFormatter.WriteLengthPrefix(buffer.Count, output);
                    output.Write(buffer.Array, buffer.Offset, buffer.Count);
                }
                else
                {
                    BinaryMessageFormatter.WriteLengthPrefix(memoryStream.Length, output);
                    memoryStream.Position = 0;
                    memoryStream.CopyTo(output);
                }
            }
        }

        private void WriteMessageCore(HubMessage message, Stream packer)
        {
            // PackerCompatibilityOptions.None prevents from serializing byte[] as strings
            // and allows extended objects
            //var packer = Packer.Create(output, PackerCompatibilityOptions.None);
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
                case CloseMessage closeMessage:
                    WriteCloseMessage(closeMessage, packer);
                    break;
                default:
                    throw new FormatException($"Unexpected message type: {message.GetType().Name}");
            }
        }

        private void WriteInvocationMessage(InvocationMessage message, Stream packer)
        {
            MessagePackBinary.WriteArrayHeader(packer, 5);
            //packer.PackArrayHeader(5);
            //packer.Pack(HubProtocolConstants.InvocationMessageType);
            MessagePackBinary.WriteInt16(packer, HubProtocolConstants.InvocationMessageType);
            PackHeaders(packer, message.Headers);
            if (string.IsNullOrEmpty(message.InvocationId))
            {
                //packer.PackNull();
                MessagePackBinary.WriteNil(packer);
            }
            else
            {
                //packer.PackString(message.InvocationId);
                MessagePackBinary.WriteString(packer, message.InvocationId);
            }
            //packer.PackString(message.Target);
            MessagePackBinary.WriteString(packer, message.Target);
            //packer.PackObject(message.Arguments, SerializationContext);
            MessagePackSerializer.Serialize(packer, message.Arguments);
        }

        private void WriteStreamInvocationMessage(StreamInvocationMessage message, Stream packer)
        {
            MessagePackBinary.WriteMapHeader(packer, 5);
            //packer.PackArrayHeader(5);
            //packer.Pack(HubProtocolConstants.StreamInvocationMessageType);
            MessagePackBinary.WriteInt16(packer, HubProtocolConstants.StreamInvocationMessageType);
            PackHeaders(packer, message.Headers);
            //packer.PackString(message.InvocationId);
            MessagePackBinary.WriteString(packer, message.InvocationId);
            //packer.PackString(message.Target);
            MessagePackBinary.WriteString(packer, message.Target);
            //packer.PackObject(message.Arguments, SerializationContext);
            MessagePackSerializer.Serialize(packer, message.Arguments);
        }

        private void WriteStreamingItemMessage(StreamItemMessage message, Stream packer)
        {
            //packer.PackArrayHeader(4);
            MessagePackBinary.WriteArrayHeader(packer, 4);
            //packer.Pack(HubProtocolConstants.StreamItemMessageType);
            MessagePackBinary.WriteInt16(packer, HubProtocolConstants.StreamItemMessageType);
            PackHeaders(packer, message.Headers);
            //packer.PackString(message.InvocationId);
            MessagePackBinary.WriteString(packer, message.InvocationId);
            //packer.PackObject(message.Item, SerializationContext);
            MessagePackSerializer.Serialize(packer, message.Item);
        }

        private void WriteCompletionMessage(CompletionMessage message, Stream packer)
        {
            var resultKind =
                message.Error != null ? ErrorResult :
                message.HasResult ? NonVoidResult :
                VoidResult;

            //packer.PackArrayHeader(4 + (resultKind != VoidResult ? 1 : 0));
            MessagePackBinary.WriteArrayHeader(packer, 4 + (resultKind != VoidResult ? 1 : 0));
            //packer.Pack(HubProtocolConstants.CompletionMessageType);
            MessagePackBinary.WriteInt16(packer, HubProtocolConstants.CompletionMessageType);
            PackHeaders(packer, message.Headers);
            //packer.PackString(message.InvocationId);
            MessagePackBinary.WriteString(packer, message.InvocationId);
            //packer.Pack(resultKind);
            MessagePackBinary.WriteInt32(packer, resultKind);
            switch (resultKind)
            {
                case ErrorResult:
                    //packer.PackString(message.Error);
                    MessagePackBinary.WriteString(packer, message.Error);
                    break;
                case NonVoidResult:
                    //packer.PackObject(message.Result, SerializationContext);
                    MessagePackSerializer.Serialize(packer, message.Result);
                    break;
            }
        }

        private void WriteCancelInvocationMessage(CancelInvocationMessage message, Stream packer)
        {
            //packer.PackArrayHeader(3);
            MessagePackBinary.WriteArrayHeader(packer, 3);
            //packer.Pack(HubProtocolConstants.CancelInvocationMessageType);
            MessagePackBinary.WriteInt16(packer, HubProtocolConstants.CancelInvocationMessageType);
            PackHeaders(packer, message.Headers);
            //packer.PackString(message.InvocationId);
            MessagePackBinary.WriteString(packer, message.InvocationId);
        }

        private void WriteCloseMessage(CloseMessage message, Stream packer)
        {
            //packer.PackArrayHeader(2);
            MessagePackBinary.WriteArrayHeader(packer, 2);
            //packer.Pack(HubProtocolConstants.CloseMessageType);
            MessagePackBinary.WriteInt16(packer, HubProtocolConstants.CloseMessageType);
            if (string.IsNullOrEmpty(message.Error))
            {
                //packer.PackNull();
                MessagePackBinary.WriteNil();
            }
            else
            {
                //packer.PackString(message.Error);
                MessagePackBinary.WriteString(message.Error);
            }
        }

        private void WritePingMessage(PingMessage pingMessage, Stream packer)
        {
            //packer.PackArrayHeader(1);
            MessagePackBinary.WriteArrayHeader(packer, 1);
            //packer.Pack(HubProtocolConstants.PingMessageType);
            MessagePackBinary.WriteInt16(packer, HubProtocolConstants.PingMessageType);
        }

        private void PackHeaders(Stream packer, IDictionary<string, string> headers)
        {
            if (headers != null)
            {
                MessagePackBinary.WriteArrayHeader(packer, headers.Count);
                //packer.PackMapHeader(headers.Count);
                if (headers.Count > 0)
                {
                    foreach (var header in headers)
                    {
                        //packer.PackString(header.Key);
                        MessagePackBinary.WriteString(packer, header.Key);
                        //packer.PackString(header.Value);
                        MessagePackBinary.WriteString(packer, header.Value);
                    }
                }
            }
            else
            {
                //packer.PackMapHeader(0);
                MessagePackBinary.WriteArrayHeader(packer, 0);
            }
        }

        private static string ReadInvocationId(Stream unpacker)
        {
            return ReadString(unpacker, "invocationId");
        }

        private static int ReadInt32(Stream unpacker, string field)
        {
            Exception msgPackException = null;
            try
            {
                return MessagePackBinary.ReadInt32(unpacker);
                /*if (unpacker.ReadInt32(out var value))
                {
                    return value;
                }*/
            }
            catch (Exception e)
            {
                msgPackException = e;
            }

            throw new FormatException($"Reading '{field}' as Int32 failed.", msgPackException);
        }

        private static string ReadString(Stream unpacker, string field)
        {
            Exception msgPackException = null;
            try
            {
                return MessagePackBinary.ReadString(unpacker);
                /*if (unpacker.Read())
                {
                    if (unpacker.LastReadData.IsNil)
                    {
                        return null;
                    }
                    else
                    {
                        return unpacker.LastReadData.AsString();
                    }
                }*/
            }
            catch (Exception e)
            {
                msgPackException = e;
            }

            throw new FormatException($"Reading '{field}' as String failed.", msgPackException);
        }

        private static bool ReadBoolean(Stream unpacker, string field)
        {
            Exception msgPackException = null;
            try
            {
                return MessagePackBinary.ReadBoolean(unpacker);
                //if (unpacker.ReadBoolean(out var value))
                //{
                //    return value;
                //}
            }
            catch (Exception e)
            {
                msgPackException = e;
            }

            throw new FormatException($"Reading '{field}' as Boolean failed.", msgPackException);
        }

        private static long ReadMapLength(Stream unpacker, string field)
        {
            Exception msgPackException = null;
            try
            {
                return MessagePackBinary.ReadMapHeader(unpacker);
                //if (unpacker.ReadMapLength(out var value))
                {
                  //  return value;
                }
            }
            catch (Exception e)
            {
                msgPackException = e;
            }

            throw new FormatException($"Reading map length for '{field}' failed.", msgPackException);
        }

        private static long ReadArrayLength(Stream unpacker, string field)
        {
            Exception msgPackException = null;
            try
            {
                return MessagePackBinary.ReadArrayHeader(unpacker);
                //if (unpacker.ReadArrayLength(out var value))
                //{
                //    return value;
                //}
            }
            catch (Exception e)
            {
                msgPackException = e;
            }

            throw new FormatException($"Reading array length for '{field}' failed.", msgPackException);
        }

        private static object DeserializeObject(Stream unpacker, Type type, string field)
        {
            Exception msgPackException = null;
            try
            {
                return MessagePackSerializer.Deserialize<object>(unpacker);
                //if (unpacker.Read())
                //{
                //    var serializer = MessagePackSerializer.Get(type);
                //    return serializer.UnpackFrom(unpacker);
                //}
            }
            catch (Exception ex)
            {
                msgPackException = ex;
            }

            throw new FormatException($"Deserializing object of the `{type.Name}` type for '{field}' failed.", msgPackException);
        }

        //internal static SerializationContext CreateDefaultSerializationContext()
        //{
        //    // serializes objects (here: arguments and results) as maps so that property names are preserved
        //    var serializationContext = new SerializationContext { SerializationMethod = SerializationMethod.Map };

        //    // allows for serializing objects that cannot be deserialized due to the lack of the default ctor etc.
        //    serializationContext.CompatibilityOptions.AllowAsymmetricSerializer = true;
        //    return serializationContext;
        //}
    }
}
