// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Microsoft.AspNetCore.SignalR.Tests
{
    public class HubConnectionHandlerTestUtils
    {
        public static Type GetConnectionHandlerType(Type hubType)
        {
            var connectionHandlerType = typeof(HubConnectionHandler<>);
            return connectionHandlerType.MakeGenericType(hubType);
        }

        public static Type GetGenericType(Type genericType, Type hubType)
        {
            return genericType.MakeGenericType(hubType);
        }

        public static void AssertHubMessage(HubMessage expected, HubMessage actual)
        {
            // We aren't testing InvocationIds here
            switch (expected)
            {
                case CompletionMessage expectedCompletion:
                    var actualCompletion = Assert.IsType<CompletionMessage>(actual);
                    Assert.Equal(expectedCompletion.Error, actualCompletion.Error);
                    Assert.Equal(expectedCompletion.HasResult, actualCompletion.HasResult);
                    Assert.Equal(expectedCompletion.Result, actualCompletion.Result);
                    break;
                case StreamItemMessage expectedStreamItem:
                    var actualStreamItem = Assert.IsType<StreamItemMessage>(actual);
                    Assert.Equal(expectedStreamItem.Item, actualStreamItem.Item);
                    break;
                case InvocationMessage expectedInvocation:
                    var actualInvocation = Assert.IsType<InvocationMessage>(actual);

                    // Either both must have non-null invocationIds or both must have null invocation IDs. Checking the exact value is NOT desired here though as it could be randomly generated
                    Assert.True((expectedInvocation.InvocationId == null && actualInvocation.InvocationId == null) ||
                        (expectedInvocation.InvocationId != null && actualInvocation.InvocationId != null));
                    Assert.Equal(expectedInvocation.Target, actualInvocation.Target);
                    Assert.Equal(expectedInvocation.Arguments, actualInvocation.Arguments);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported Hub Message type {expected.GetType()}");
            }
        }

        public static IServiceProvider CreateServiceProvider(Action<ServiceCollection> addServices = null)
        {
            var services = new ServiceCollection();
            services.AddOptions()
                .AddLogging();

            services.AddSignalR()
                .AddMessagePackProtocol();

            addServices?.Invoke(services);

            return services.BuildServiceProvider();
        }

        public static Connections.ConnectionHandler GetHubConnectionHandler(Type hubType)
        {
            var serviceProvider = CreateServiceProvider();
            return (Connections.ConnectionHandler)serviceProvider.GetService(GetConnectionHandlerType(hubType));
        }
    }

    public class Result
    {
        public string Message { get; set; }
#pragma warning disable IDE1006 // Naming Styles
        // testing casing
        public string paramName { get; set; }
#pragma warning restore IDE1006 // Naming Styles
    }

    public class TrackDispose
    {
        public int DisposeCount = 0;
    }
}
