// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Channels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.SignalR.Internal;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Microsoft.AspNetCore.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.SignalR
{
    public class HubEndPoint<THub> where THub : Hub
    {
        private readonly Dictionary<string, HubMethodDescriptor> _methods = new Dictionary<string, HubMethodDescriptor>(StringComparer.OrdinalIgnoreCase);

        private readonly HubLifetimeManager<THub> _lifetimeManager;
        private readonly IHubContext<THub> _hubContext;
        private readonly ILogger<HubEndPoint<THub>> _logger;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly IHubProtocolResolver _protocolResolver;

        public HubEndPoint(HubLifetimeManager<THub> lifetimeManager,
                           IHubProtocolResolver protocolResolver,
                           IHubContext<THub> hubContext,
                           ILogger<HubEndPoint<THub>> logger,
                           IServiceScopeFactory serviceScopeFactory)
        {
            _protocolResolver = protocolResolver;
            _lifetimeManager = lifetimeManager;
            _hubContext = hubContext;
            _logger = logger;
            _serviceScopeFactory = serviceScopeFactory;

            DiscoverHubMethods();
        }

        public async Task OnConnectedAsync(ConnectionContext connection)
        {
            try
            {
                // Resolve the Hub Protocol for the connection and store it in metadata
                // Other components, outside the Hub, may need to know what protocol is in use
                // for a particular connection, so we store it here.
                var protocol = _protocolResolver.GetProtocol(connection);
                connection.Metadata[HubConnectionMetadataNames.HubProtocol] = protocol;

                await _lifetimeManager.OnConnectedAsync(connection);
                await RunHubAsync(connection, protocol);
            }
            finally
            {
                await _lifetimeManager.OnDisconnectedAsync(connection);
            }
        }

        private async Task RunHubAsync(ConnectionContext connection, IHubProtocol protocol)
        {
            var hubConnection = new HubConnection(connection, protocol, _logger);
            connection.Metadata[typeof(HubConnection)] = hubConnection;

            // TODO: Don't register this per connection
            foreach (var method in _methods)
            {
                var descriptor = method.Value;
                var methodExecutor = method.Value.MethodExecutor;

                hubConnection.On(method.Key, method.Value.ParameterTypes, method.Value.MethodExecutor.MethodReturnType, async args =>
                {
                    using (var scope = _serviceScopeFactory.CreateScope())
                    {
                        if (!await IsHubMethodAuthorized(scope.ServiceProvider, connection.User, descriptor.Policies))
                        {
                            _logger.LogDebug("Failed to invoke {hubMethod} because user is unauthorized", method.Key);

                            throw new InvalidOperationException($"Failed to invoke '{method.Key}' because user is unauthorized");
                        }

                        var hubActivator = scope.ServiceProvider.GetRequiredService<IHubActivator<THub>>();
                        var hub = hubActivator.Create();

                        InitializeHub(hub, connection);

                        try
                        {
                            object result = null;

                            // ReadableChannel is awaitable but we don't want to await it.
                            if (methodExecutor.IsMethodAsync && !IsChannel(methodExecutor.MethodReturnType, out _))
                            {
                                if (methodExecutor.MethodReturnType == typeof(Task))
                                {
                                    await (Task)methodExecutor.Execute(hub, args);
                                }
                                else
                                {
                                    result = await methodExecutor.ExecuteAsync(hub, args);
                                }
                            }
                            else
                            {
                                result = methodExecutor.Execute(hub, args);
                            }

                            return result;
                        }
                        finally
                        {
                            hubActivator.Release(hub);
                        }
                    }
                });
            }

            hubConnection.Connected += async () =>
            {
                await HubOnConnectedAsync(connection);
            };

            await hubConnection.StartAsync();

            var tcs = new TaskCompletionSource<object>();

            hubConnection.Closed += async error =>
            {
                _logger.LogError(0, error, "Error when processing requests.");

                await HubOnDisconnectedAsync(connection, error);

                if (error != null)
                {
                    tcs.TrySetException(error);
                }
                else
                {
                    tcs.TrySetResult(null);
                }
            };

            await tcs.Task;
        }

        private async Task<bool> IsHubMethodAuthorized(IServiceProvider provider, ClaimsPrincipal principal, IList<IAuthorizeData> policies)
        {
            // If there are no policies we don't need to run auth
            if (!policies.Any())
            {
                return true;
            }

            var authService = provider.GetRequiredService<IAuthorizationService>();
            var policyProvider = provider.GetRequiredService<IAuthorizationPolicyProvider>();

            var authorizePolicy = await AuthorizationPolicy.CombineAsync(policyProvider, policies);
            // AuthorizationPolicy.CombineAsync only returns null if there are no policies and we check that above
            Debug.Assert(authorizePolicy != null);

            var authorizationResult = await authService.AuthorizeAsync(principal, authorizePolicy);
            // Only check authorization success, challenge or forbid wouldn't make sense from a hub method invocation
            return authorizationResult.Succeeded;
        }

        private static bool IsChannel(Type type, out Type payloadType)
        {
            var channelType = type.AllBaseTypes().FirstOrDefault(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(ReadableChannel<>));
            if (channelType == null)
            {
                payloadType = null;
                return false;
            }
            else
            {
                payloadType = channelType.GetGenericArguments()[0];
                return true;
            }
        }

        private async Task HubOnConnectedAsync(ConnectionContext connection)
        {
            try
            {
                using (var scope = _serviceScopeFactory.CreateScope())
                {
                    var hubActivator = scope.ServiceProvider.GetRequiredService<IHubActivator<THub>>();
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

        private async Task HubOnDisconnectedAsync(ConnectionContext connection, Exception exception)
        {
            try
            {
                using (var scope = _serviceScopeFactory.CreateScope())
                {
                    var hubActivator = scope.ServiceProvider.GetRequiredService<IHubActivator<THub>>();
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

        private void InitializeHub(THub hub, ConnectionContext connection)
        {
            hub.Clients = _hubContext.Clients;
            hub.Context = new HubCallerContext(connection);
            hub.Groups = new GroupManager<THub>(_lifetimeManager);
        }

        private void DiscoverHubMethods()
        {
            var hubType = typeof(THub);
            var hubTypeInfo = hubType.GetTypeInfo();

            foreach (var methodInfo in HubReflectionHelper.GetHubMethods(hubType))
            {
                var methodName = methodInfo.Name;

                if (_methods.ContainsKey(methodName))
                {
                    throw new NotSupportedException($"Duplicate definitions of '{methodName}'. Overloading is not supported.");
                }

                var executor = ObjectMethodExecutor.Create(methodInfo, hubTypeInfo);
                var authorizeAttributes = methodInfo.GetCustomAttributes<AuthorizeAttribute>(inherit: true);
                _methods[methodName] = new HubMethodDescriptor(executor, authorizeAttributes);

                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug("Hub method '{methodName}' is bound", methodName);
                }
            }
        }

        // REVIEW: We can decide to move this out of here if we want pluggable hub discovery
        private class HubMethodDescriptor
        {
            public HubMethodDescriptor(ObjectMethodExecutor methodExecutor, IEnumerable<IAuthorizeData> policies)
            {
                MethodExecutor = methodExecutor;
                ParameterTypes = methodExecutor.MethodParameters.Select(p => p.ParameterType).ToArray();
                Policies = policies.ToArray();
            }

            public ObjectMethodExecutor MethodExecutor { get; }

            public Type[] ParameterTypes { get; }

            public IList<IAuthorizeData> Policies { get; }
        }
    }
}
