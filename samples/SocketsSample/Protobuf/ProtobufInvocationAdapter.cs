// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

namespace SocketsSample.Protobuf
{
    public class ProtobufInvocationAdapter : IInvocationAdapter
    {
        private IServiceProvider _serviceProvider;

        public ProtobufInvocationAdapter(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public Task<InvocationMessage> ReadMessageAsync(Stream stream, IInvocationBinder binder, CancellationToken cancellationToken)
        {
            return Task.Run(() => CreateInvocationMessageInt(stream, binder));
        }

        public Task WriteMessageAsync(InvocationMessage message, Stream stream, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        private Task<InvocationMessage> CreateInvocationMessageInt(Stream stream, IInvocationBinder binder)
        {
            var inputStream = new CodedInputStream(stream, leaveOpen: true);
            var messageKind = new RpcMessageKind();
            inputStream.ReadMessage(messageKind);
            if(messageKind.MessageKind == RpcMessageKind.Types.Kind.Invocation)
            {
                return CreateInvocationDescriptorInt(inputStream, binder);
            }
            else
            {
                return CreateInvocationResultDescriptorInt(inputStream, binder);
            }
        }

        private Task<InvocationMessage> CreateInvocationResultDescriptorInt(CodedInputStream inputStream, IInvocationBinder binder)
        {
            throw new NotImplementedException("Not yet implemented for Protobuf");
        }

        private Task<InvocationMessage> CreateInvocationDescriptorInt(CodedInputStream inputStream, IInvocationBinder binder)
        {
            var invocationHeader = new RpcInvocationHeader();
            inputStream.ReadMessage(invocationHeader);
            var argumentTypes = binder.GetParameterTypes(invocationHeader.Name);

            var invocationDescriptor = new InvocationDescriptor();
            invocationDescriptor.Method = invocationHeader.Name;
            invocationDescriptor.Id = invocationHeader.Id.ToString();
            invocationDescriptor.Arguments = new object[argumentTypes.Length];

            var primitiveParser = PrimitiveValue.Parser;

            for (var i = 0; i < argumentTypes.Length; i++)
            {
                if (typeof(int) == argumentTypes[i])
                {
                    var value = new PrimitiveValue();
                    inputStream.ReadMessage(value);
                    invocationDescriptor.Arguments[i] = value.Int32Value;
                }
                else if (typeof(string) == argumentTypes[i])
                {
                    var value = new PrimitiveValue();
                    inputStream.ReadMessage(value);
                    invocationDescriptor.Arguments[i] = value.StringValue;
                }
                else
                {
                    var serializer = _serviceProvider.GetRequiredService<ProtobufSerializer>();
                    invocationDescriptor.Arguments[i] = serializer.GetValue(inputStream, argumentTypes[i]);
                }
            }

            return Task.FromResult<InvocationMessage>(invocationDescriptor);
        }

        public async Task WriteInvocationResultAsync(InvocationResultDescriptor resultDescriptor, Stream stream)
        {
            var outputStream = new CodedOutputStream(stream, leaveOpen: true);
            outputStream.WriteMessage(new RpcMessageKind() { MessageKind = RpcMessageKind.Types.Kind.Result });

            var resultHeader = new RpcInvocationResultHeader
            {
                Id = int.Parse(resultDescriptor.Id),
                HasResult = resultDescriptor.Result != null
            };

            if (resultDescriptor.Error != null)
            {
                resultHeader.Error = resultDescriptor.Error;
            }

            outputStream.WriteMessage(resultHeader);

            if (string.IsNullOrEmpty(resultHeader.Error) && resultDescriptor.Result != null)
            {
                var result = resultDescriptor.Result;

                if (result.GetType() == typeof(int))
                {
                    outputStream.WriteMessage(new PrimitiveValue { Int32Value = (int)result });
                }
                else if (result.GetType() == typeof(string))
                {
                    outputStream.WriteMessage(new PrimitiveValue { StringValue = (string)result });
                }
                else
                {
                    var serializer = _serviceProvider.GetRequiredService<ProtobufSerializer>();
                    var message = serializer.GetMessage(result);
                    outputStream.WriteMessage(message);
                }
            }

            outputStream.Flush();
            await stream.FlushAsync();
        }

        public async Task WriteInvocationDescriptorAsync(InvocationDescriptor invocationDescriptor, Stream stream)
        {
            var outputStream = new CodedOutputStream(stream, leaveOpen: true);
            outputStream.WriteMessage(new RpcMessageKind() { MessageKind = RpcMessageKind.Types.Kind.Invocation });

            var invocationHeader = new RpcInvocationHeader()
            {
                Id = 0,
                Name = invocationDescriptor.Method,
                NumArgs = invocationDescriptor.Arguments.Length
            };

            outputStream.WriteMessage(invocationHeader);

            foreach (var arg in invocationDescriptor.Arguments)
            {
                if (arg.GetType() == typeof(int))
                {
                    outputStream.WriteMessage(new PrimitiveValue { Int32Value = (int)arg });
                }
                else if (arg.GetType() == typeof(string))
                {
                    outputStream.WriteMessage(new PrimitiveValue { StringValue = (string)arg });
                }
                else
                {
                    var serializer = _serviceProvider.GetRequiredService<ProtobufSerializer>();
                    var message = serializer.GetMessage(arg);
                    outputStream.WriteMessage(message);
                }
            }

            outputStream.Flush();
            await stream.FlushAsync();
        }
    }
}
