// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Channels;
using Microsoft.AspNetCore.SignalR.Internal;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Microsoft.AspNetCore.Sockets;
using Microsoft.AspNetCore.Sockets.Internal;

namespace Microsoft.AspNetCore.SignalR.Tests
{
    public class TestClient : IDisposable, IInvocationBinder
    {
        private static int _id;
        private IHubProtocol _protocol;
        private CancellationTokenSource _cts;

        public Connection Connection;
        public IChannelConnection<Message> Application { get; }
        public Task Connected => Connection.Metadata.Get<TaskCompletionSource<bool>>("ConnectedTask").Task;

        public TestClient(IServiceProvider serviceProvider, string format = "json")
        {
            var transportToApplication = Channel.CreateUnbounded<Message>();
            var applicationToTransport = Channel.CreateUnbounded<Message>();

            Application = ChannelConnection.Create<Message>(input: applicationToTransport, output: transportToApplication);
            var transport = ChannelConnection.Create<Message>(input: transportToApplication, output: applicationToTransport);

            Connection = new Connection(Guid.NewGuid().ToString(), transport);
            Connection.Metadata["formatType"] = format;
            Connection.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, Interlocked.Increment(ref _id).ToString()) }));
            Connection.Metadata["ConnectedTask"] = new TaskCompletionSource<bool>();

            _protocol = new JsonHubProtocol();

            _cts = new CancellationTokenSource();
        }

        public async Task<IEnumerable<ResultMessage>> InvokeAndWait(string methodName, params object[] args)
        {
            var invocationId = await Invoke(methodName, args);

            var results = new List<ResultMessage>();
            while (true)
            {
                var message = await Read();

                if (!string.Equals(message.InvocationId, invocationId))
                {
                    throw new NotSupportedException("TestClient does not support multiple outgoing invocations!");
                }

                if (message == null)
                {
                    throw new InvalidOperationException("Connection aborted!");
                }

                switch (message)
                {
                    case ResultMessage result:
                        results.Add(result);
                        break;
                    case CompletionMessage completion:
                        return results;
                    default:
                        throw new NotSupportedException("TestClient does not support receiving invocations!");
                }
            }
        }

        public async Task<string> Invoke(string methodName, params object[] args)
        {
            var invocationId = GetInvocationId();
            var payload = _protocol.WriteMessage(new InvocationMessage(invocationId, methodName, args));

            await Application.Output.WriteAsync(new Message(payload, MessageType.Binary, endOfMessage: true));

            return invocationId;
        }

        public async Task<HubMessage> Read()
        {
            while(true)
            {
                var message = TryRead();

                if(message == null)
                {
                    if(!await Application.Input.WaitToReadAsync())
                    {
                        return null;
                    }
                }
                else
                {
                    return message;
                }
            }
        }

        public HubMessage TryRead()
        {
            if (Application.Input.TryRead(out var message))
            {
                if (!_protocol.TryParseMessage(message.Payload, this, out var hubMessage))
                {
                    throw new InvalidOperationException("Received invalid message");
                }
                else
                {
                    return hubMessage;
                }
            }
            return null;
        }

        public void Dispose()
        {
            _cts.Cancel();
            Connection.Dispose();
        }

        private static string GetInvocationId()
        {
            return Guid.NewGuid().ToString("N");
        }

        Type[] IInvocationBinder.GetParameterTypes(string methodName)
        {
            // TODO: Possibly support actual client methods
            return new[] { typeof(object) };
        }

        Type IInvocationBinder.GetReturnType(string invocationId)
        {
            return typeof(object);
        }
    }
}
