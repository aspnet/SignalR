﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.IO.Pipelines.Text.Primitives;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Internal;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Microsoft.AspNetCore.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.AspNetCore.SignalR
{
    public class HubEndPoint<THub> : HubEndPoint<THub, IClientProxy> where THub : Hub<IClientProxy>
    {
        public HubEndPoint(HubLifetimeManager<THub> lifetimeManager,
                           IHubContext<THub> hubContext,
                           IOptions<EndPointOptions<HubEndPoint<THub, IClientProxy>>> endPointOptions,
                           ILogger<HubEndPoint<THub>> logger,
                           IServiceScopeFactory serviceScopeFactory)
            : base(lifetimeManager, hubContext, endPointOptions, logger, serviceScopeFactory)
        {
        }
    }

    public class HubEndPoint<THub, TClient> : EndPoint, IInvocationBinder where THub : Hub<TClient>
    {
        private readonly Dictionary<string, HubMethodDescriptor> _methods = new Dictionary<string, HubMethodDescriptor>(StringComparer.OrdinalIgnoreCase);

        private readonly HubLifetimeManager<THub> _lifetimeManager;
        private readonly IHubContext<THub, TClient> _hubContext;
        private readonly ILogger<HubEndPoint<THub, TClient>> _logger;
        private readonly IServiceScopeFactory _serviceScopeFactory;

        // TODO: Configuration and Protobuf
        private readonly JsonHubProtocol _protocol = new JsonHubProtocol();

        public HubEndPoint(HubLifetimeManager<THub> lifetimeManager,
                           IHubContext<THub, TClient> hubContext,
                           IOptions<EndPointOptions<HubEndPoint<THub, TClient>>> endPointOptions,
                           ILogger<HubEndPoint<THub, TClient>> logger,
                           IServiceScopeFactory serviceScopeFactory)
        {
            _lifetimeManager = lifetimeManager;
            _hubContext = hubContext;
            _logger = logger;
            _serviceScopeFactory = serviceScopeFactory;

            DiscoverHubMethods();
        }

        public override async Task OnConnectedAsync(Connection connection)
        {
            try
            {
                await _lifetimeManager.OnConnectedAsync(connection);
                await RunHubAsync(connection);
            }
            finally
            {
                await _lifetimeManager.OnDisconnectedAsync(connection);
            }
        }

        private async Task RunHubAsync(Connection connection)
        {
            await HubOnConnectedAsync(connection);

            try
            {
                await DispatchMessagesAsync(connection);
            }
            catch (Exception ex)
            {
                _logger.LogError(0, ex, "Error when processing requests.");
                await HubOnDisconnectedAsync(connection, ex);
                throw;
            }

            await HubOnDisconnectedAsync(connection, null);
        }

        private async Task HubOnConnectedAsync(Connection connection)
        {
            try
            {
                using (var scope = _serviceScopeFactory.CreateScope())
                {
                    var hubActivator = scope.ServiceProvider.GetRequiredService<IHubActivator<THub, TClient>>();
                    var hub = hubActivator.Create();
                    try
                    {
                        InitializeHub(hub, connection);
                        await hub.OnConnectedAsync();
                    }
                    finally
                    {
                        hubActivator.Release(hub);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(0, ex, "Error when invoking OnConnectedAsync on hub.");
                throw;
            }
        }

        private async Task HubOnDisconnectedAsync(Connection connection, Exception exception)
        {
            try
            {
                using (var scope = _serviceScopeFactory.CreateScope())
                {
                    var hubActivator = scope.ServiceProvider.GetRequiredService<IHubActivator<THub, TClient>>();
                    var hub = hubActivator.Create();
                    try
                    {
                        InitializeHub(hub, connection);
                        await hub.OnDisconnectedAsync(exception);
                    }
                    finally
                    {
                        hubActivator.Release(hub);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(0, ex, "Error when invoking OnDisconnectedAsync on hub.");
                throw;
            }
        }

        private async Task DispatchMessagesAsync(Connection connection)
        {
            // We use these for error handling. Since we dispatch multiple hub invocations
            // in parallel, we need a way to communicate failure back to the main processing loop. The
            // cancellation token is used to stop reading from the channel, the tcs
            // is used to get the exception so we can bubble it up the stack
            var cts = new CancellationTokenSource();
            var completion = new TaskCompletionSource<object>();

            try
            {
                while (await connection.Transport.Input.WaitToReadAsync(cts.Token))
                {
                    while (connection.Transport.Input.TryRead(out var incomingMessage))
                    {
                        if (!_protocol.TryParseMessage(incomingMessage.Payload, this, out var hubMessage))
                        {
                            _logger.LogError("Received invalid message");
                            throw new InvalidOperationException("Received invalid message");
                        }

                        switch (hubMessage)
                        {
                            case InvocationMessage invocationMessage:
                                if (_logger.IsEnabled(LogLevel.Debug))
                                {
                                    _logger.LogDebug("Received hub invocation: {invocation}", invocationMessage);
                                }

                                // Don't wait on the result of execution, continue processing other
                                // incoming messages on this connection.
                                var ignore = ProcessInvocation(connection, invocationMessage, cts, completion);
                                break;
                            default:
                                _logger.LogError("Received unsupported message of type '{messageType}'", hubMessage.GetType().FullName);
                                throw new NotSupportedException($"Received unsupported message: {hubMessage}");
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Await the task so the exception bubbles up to the caller
                await completion.Task;
            }
        }

        private async Task ProcessInvocation(Connection connection,
                                             InvocationMessage invocationMessage,
                                             CancellationTokenSource dispatcherCancellation,
                                             TaskCompletionSource<object> dispatcherCompletion)
        {
            try
            {
                // If an unexpected exception occurs then we want to kill the entire connection
                // by ending the processing loop
                await Execute(connection, invocationMessage);
            }
            catch (Exception ex)
            {
                // Set the exception on the task completion source
                dispatcherCompletion.TrySetException(ex);

                // Cancel reading operation
                dispatcherCancellation.Cancel();
            }
        }

        private async Task Execute(Connection connection, InvocationMessage invocationMessage)
        {
            HubMethodDescriptor descriptor;
            if (!_methods.TryGetValue(invocationMessage.Target, out descriptor))
            {
                // Send an error to the client. Then let the normal completion process occur
                _logger.LogError("Unknown hub method '{method}'", invocationMessage.Target);
                await SendMessageAsync(connection, CompletionMessage.WithError(invocationMessage.InvocationId, $"Unknown hub method '{invocationMessage.Target}'"));
            }
            else
            {
                var result = await Invoke(descriptor, connection, invocationMessage);
                await SendMessageAsync(connection, result);
            }
        }

        private async Task SendMessageAsync(Connection connection, HubMessage hubMessage)
        {
            var payload = await _protocol.WriteToArrayAsync(hubMessage);
            var message = new Message(payload, _protocol.MessageType, endOfMessage: true);

            while (await connection.Transport.Output.WaitToWriteAsync())
            {
                if (connection.Transport.Output.TryWrite(message))
                {
                    return;
                }
            }

            // Output is closed. Cancel this invocation completely
            _logger.LogWarning("Outbound channel was closed while trying to write hub message");
            throw new OperationCanceledException("Outbound channel was closed while trying to write hub message");
        }

        private async Task<CompletionMessage> Invoke(HubMethodDescriptor descriptor, Connection connection, InvocationMessage invocationMessage)
        {
            var methodExecutor = descriptor.MethodExecutor;

            using (var scope = _serviceScopeFactory.CreateScope())
            {
                var hubActivator = scope.ServiceProvider.GetRequiredService<IHubActivator<THub, TClient>>();
                var hub = hubActivator.Create();

                try
                {
                    InitializeHub(hub, connection);

                    object result = null;
                    if (methodExecutor.IsMethodAsync)
                    {
                        if (methodExecutor.TaskGenericType == null)
                        {
                            await (Task)methodExecutor.Execute(hub, invocationMessage.Arguments);
                        }
                        else
                        {
                            result = await methodExecutor.ExecuteAsync(hub, invocationMessage.Arguments);
                        }
                    }
                    else
                    {
                        result = methodExecutor.Execute(hub, invocationMessage.Arguments);
                    }

                    return CompletionMessage.WithResult(invocationMessage.InvocationId, result);
                }
                catch (TargetInvocationException ex)
                {
                    _logger.LogError(0, ex, "Failed to invoke hub method");
                    return CompletionMessage.WithError(invocationMessage.InvocationId, ex.InnerException.Message);
                }
                catch (Exception ex)
                {
                    _logger.LogError(0, ex, "Failed to invoke hub method");
                    return CompletionMessage.WithError(invocationMessage.InvocationId, ex.Message);
                }
                finally
                {
                    hubActivator.Release(hub);
                }
            }
        }

        private void InitializeHub(THub hub, Connection connection)
        {
            hub.Clients = _hubContext.Clients;
            hub.Context = new HubCallerContext(connection);
            hub.Groups = new GroupManager<THub>(connection, _lifetimeManager);
        }

        private void DiscoverHubMethods()
        {
            var hubType = typeof(THub);
            foreach (var methodInfo in hubType.GetMethods().Where(m => IsHubMethod(m)))
            {
                var methodName = methodInfo.Name;

                if (_methods.ContainsKey(methodName))
                {
                    throw new NotSupportedException($"Duplicate definitions of '{methodName}'. Overloading is not supported.");
                }

                var executor = ObjectMethodExecutor.Create(methodInfo, hubType.GetTypeInfo());
                _methods[methodName] = new HubMethodDescriptor(executor);

                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug("Hub method '{methodName}' is bound", methodName);
                }
            }
        }

        private static bool IsHubMethod(MethodInfo methodInfo)
        {
            // TODO: Add more checks
            if (!methodInfo.IsPublic || methodInfo.IsSpecialName)
            {
                return false;
            }

            var baseDefinition = methodInfo.GetBaseDefinition().DeclaringType;
            var baseType = baseDefinition.GetTypeInfo().IsGenericType ? baseDefinition.GetGenericTypeDefinition() : baseDefinition;
            if (typeof(Hub<>) == baseType)
            {
                return false;
            }

            return true;
        }

        Type IInvocationBinder.GetReturnType(string invocationId)
        {
            return typeof(object);
        }

        Type[] IInvocationBinder.GetParameterTypes(string methodName)
        {
            HubMethodDescriptor descriptor;
            if (!_methods.TryGetValue(methodName, out descriptor))
            {
                return Type.EmptyTypes;
            }
            return descriptor.ParameterTypes;
        }

        // REVIEW: We can decide to move this out of here if we want pluggable hub discovery
        private class HubMethodDescriptor
        {
            public HubMethodDescriptor(ObjectMethodExecutor methodExecutor)
            {
                MethodExecutor = methodExecutor;
                ParameterTypes = methodExecutor.ActionParameters.Select(p => p.ParameterType).ToArray();
            }

            public ObjectMethodExecutor MethodExecutor { get; }

            public Type[] ParameterTypes { get; }
        }
    }
}
