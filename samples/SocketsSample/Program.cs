// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Internal;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SocketsSample.Hubs;

namespace SocketsSample
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var json = new JsonHubProtocol();
            var message = new InvocationMessage(target: "Target", argumentBindingException: null, new string('F', 10240), new byte[10240]);
            var bytes = json.WriteToArray(message);

            System.Console.WriteLine("Waiting to start");

            System.Console.ReadLine();

            int messageCount = 1000;
            var messages = new List<HubMessage>(messageCount);
            var binder = new Binder(new[] { typeof(string), typeof(byte[]) }, typeof(void));
            for (int i = 0; i < messageCount; i++)
            {
                json.TryParseMessages(bytes, binder, messages);
            }
            messages.Clear();

            Console.WriteLine("Done!");
            System.Console.ReadLine();
        }

        private class Binder : IInvocationBinder
        {
            private readonly Type[] _parameterTypes;
            private readonly Type _retunType;
            public Binder(Type[] parameterTypes, Type returnType)
            {
                _parameterTypes = parameterTypes;
                _retunType = returnType;
            }

            public IReadOnlyList<Type> GetParameterTypes(string methodName) => _parameterTypes;

            public Type GetReturnType(string invocationId) => _retunType;
        }
    }
}