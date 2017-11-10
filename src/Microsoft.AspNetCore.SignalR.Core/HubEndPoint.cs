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
using Microsoft.AspNetCore.SignalR.Core;
using Microsoft.AspNetCore.SignalR.Core.Internal;
using Microsoft.AspNetCore.SignalR.Features;
using Microsoft.AspNetCore.SignalR.Internal;
using Microsoft.AspNetCore.SignalR.Internal.Encoders;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Microsoft.AspNetCore.Sockets;
using Microsoft.AspNetCore.Sockets.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.AspNetCore.SignalR
{
    public class HubEndPoint<THub> : IInvocationBinder where THub : Hub
    {
        private static readonly Base64Encoder Base64Encoder = new Base64Encoder();
        private static readonly PassThroughEncoder PassThroughEncoder = new PassThroughEncoder();

        private readonly Dictionary<string, HubMethodDescriptor> _methods = new Dictionary<string, HubMethodDescriptor>(StringComparer.OrdinalIgnoreCase);

        private readonly HubLifetimeManager<THub> _lifetimeManager;
        private readonly IHubContext<THub> _hubContext;
        private readonly ILogger<HubEndPoint<THub>> _logger;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly IHubProtocolResolver _protocolResolver;
        private readonly HubOptions _hubOptions;
        private readonly IUserIdProvider _userIdProvider;

        public HubEndPoint(HubLifetimeManager<THub> lifetimeManager,
                           IHubProtocolResolver protocolResolver,
                           IHubContext<THub> hubContext,
                           IOptions<HubOptions> hubOptions,
                           ILogger<HubEndPoint<THub>> logger,
                           IServiceScopeFactory serviceScopeFactory,
                           IUserIdProvider userIdProvider)
        {
            _protocolResolver = protocolResolver;
            _lifetimeManager = lifetimeManager;
            _hubContext = hubContext;
            _hubOptions = hubOptions.Value;
            _logger = logger;
            _serviceScopeFactory = serviceScopeFactory;
            _userIdProvider = userIdProvider;

            DiscoverHubMethods();
        }

        public async Task OnConnectedAsync(ConnectionContext connection)
        {
            var output = Channel.CreateUnbounded<HubMessage>();

            // Set the hub feature before doing anything else. This stores
            // all the relevant state for a SignalR Hub connection.
            connection.Features.Set<IHubFeature>(new HubFeature());

            var connectionContext = new HubConnectionContext(output, connection);

            if (_hubOptions.Protocol != null)
            {
                InitializeProtocol(connectionContext, _hubOptions.Protocol);
            }
            else
            {
                if (!await ProcessNegotiate(connectionContext))
                {
                    return;
                }
            }

            connectionContext.UserIdentifier = _userIdProvider.GetUserId(connectionContext);

            // Hubs support multiple producers so we set up this loop to copy
            // data written to the HubConnectionContext's channel to the transport channel
            var protocolReaderWriter = connectionContext.ProtocolReaderWriter;
            async Task WriteToTransport()
            {
                try
                {
                    while (await output.In.WaitToReadAsync())
                    {
                        while (output.In.TryRead(out var hubMessage))
                        {
                            var buffer = protocolReaderWriter.WriteMessage(hubMessage);
                            while (await connection.Transport.Out.WaitToWriteAsync())
                            {
                                if (connection.Transport.Out.TryWrite(buffer))
                                {
                                    break;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    connectionContext.Abort(ex);
                }
            }

            var writingOutputTask = WriteToTransport();

            try
            {
                await _lifetimeManager.OnConnectedAsync(connectionContext);
                await RunHubAsync(connectionContext);
            }
            finally
            {
                await _lifetimeManager.OnDisconnectedAsync(connectionContext);

                // Nothing should be writing to the HubConnectionContext
                output.Out.TryComplete();

                // This should unwind once we complete the output
                await writingOutputTask;
            }
        }

        private async Task<bool> ProcessNegotiate(HubConnectionContext connection)
        {
            try
            {
                using (var cts = new CancellationTokenSource())
                {
                    cts.CancelAfter(_hubOptions.NegotiateTimeout);
                    while (await connection.Input.WaitToReadAsync(cts.Token))
                    {
                        while (connection.Input.TryRead(out var buffer))
                        {
                            if (NegotiationProtocol.TryParseMessage(buffer, out var negotiationMessage))
                            {
                                var protocol = _protocolResolver.GetProtocol(negotiationMessage.Protocol, connection);
                                InitializeProtocol(connection, protocol);
                                return true;
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger.NegotiateCanceled();
            }

            return false;
        }

        private void InitializeProtocol(HubConnectionContext connection, IHubProtocol protocol)
        {
            var transportCapabilities = connection.Features.Get<IConnectionTransportFeature>()?.TransportCapabilities
                ?? throw new InvalidOperationException("Unable to read transport capabilities.");

            var dataEncoder = (protocol.Type == ProtocolType.Binary && (transportCapabilities & TransferMode.Binary) == 0)
                ? (IDataEncoder)Base64Encoder
                : PassThroughEncoder;

            var transferModeFeature = connection.Features.Get<ITransferModeFeature>() ??
                throw new InvalidOperationException("Unable to read transfer mode.");

            transferModeFeature.TransferMode =
                (protocol.Type == ProtocolType.Binary && (transportCapabilities & TransferMode.Binary) != 0)
                    ? TransferMode.Binary
                    : TransferMode.Text;

            connection.ProtocolReaderWriter = new HubProtocolReaderWriter(protocol, dataEncoder);

            _logger.UsingHubProtocol(protocol.Name);
        }

        private async Task RunHubAsync(HubConnectionContext connection)
        {
            await HubOnConnectedAsync(connection);

            try
            {
                await DispatchMessagesAsync(connection);
            }
            catch (Exception ex)
            {
                _logger.ErrorProcessingRequest(ex);
                await HubOnDisconnectedAsync(connection, ex);
                throw;
            }

            await HubOnDisconnectedAsync(connection, null);
        }

        private async Task HubOnConnectedAsync(HubConnectionContext connection)
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
                _logger.ErrorInvokingHubMethod("OnConnectedAsync", ex);
                throw;
            }
        }

        private async Task HubOnDisconnectedAsync(HubConnectionContext connection, Exception exception)
        {
            try
            {
                // We wait on abort to complete, this is so that we can guarantee that all callbacks have fired
                // before OnDisconnectedAsync

                try
                {
                    // Ensure the connection is aborted before firing disconnect
                    await connection.AbortAsync();
                }
                catch (Exception ex)
                {
                    _logger.AbortFailed(ex);
                }

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
                _logger.ErrorInvokingHubMethod("OnDisconnectedAsync", ex);
                throw;
            }
        }

        private async Task DispatchMessagesAsync(HubConnectionContext connection)
        {
            // Since we dispatch multiple hub invocations in parallel, we need a way to communicate failure back to the main processing loop.
            // This is done by aborting the connection.

            try
            {
                while (await connection.Input.WaitToReadAsync(connection.ConnectionAbortedToken))
                {
                    while (connection.Input.TryRead(out var buffer))
                    {
                        if (connection.ProtocolReaderWriter.ReadMessages(buffer, this, out var hubMessages))
                        {
                            foreach (var hubMessage in hubMessages)
                            {
                                switch (hubMessage)
                                {
                                    case InvocationMessage invocationMessage:
                                        _logger.ReceivedHubInvocation(invocationMessage);

                                        // Don't wait on the result of execution, continue processing other
                                        // incoming messages on this connection.
                                        _ = ProcessInvocation(connection, invocationMessage);
                                        break;

                                    case CancelInvocationMessage cancelInvocationMessage:
                                        // Check if there is an associated active stream and cancel it if it exists.
                                        // The cts will be removed when the streaming method completes executing
                                        if (connection.ActiveRequestCancellationSources.TryGetValue(cancelInvocationMessage.InvocationId, out var cts))
                                        {
                                            _logger.CancelStream(cancelInvocationMessage.InvocationId);
                                            cts.Cancel();
                                        }
                                        else
                                        {
                                            // Stream can be canceled on the server while client is canceling stream.
                                            _logger.UnexpectedCancel();
                                        }
                                        break;

                                    // Other kind of message we weren't expecting
                                    default:
                                        _logger.UnsupportedMessageReceived(hubMessage.GetType().FullName);
                                        throw new NotSupportedException($"Received unsupported message: {hubMessage}");
                                }
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // If there's an exception, bubble it to the caller
                connection.AbortException?.Throw();
            }
        }

        private async Task ProcessInvocation(HubConnectionContext connection, InvocationMessage invocationMessage)
        {
            try
            {
                // If an unexpected exception occurs then we want to kill the entire connection
                // by ending the processing loop
                await Execute(connection, invocationMessage);
            }
            catch (Exception ex)
            {
                // Abort the entire connection if the invocation fails in an unexpected way
                connection.Abort(ex);
            }
        }

        private async Task Execute(HubConnectionContext connection, InvocationMessage invocationMessage)
        {
            if (!_methods.TryGetValue(invocationMessage.Target, out var descriptor))
            {
                // Send an error to the client. Then let the normal completion process occur
                _logger.UnknownHubMethod(invocationMessage.Target);
                await SendMessageAsync(connection, CompletionMessage.WithError(invocationMessage.InvocationId, $"Unknown hub method '{invocationMessage.Target}'"));
            }
            else
            {
                await Invoke(descriptor, connection, invocationMessage);
            }
        }

        private async Task SendMessageAsync(HubConnectionContext connection, HubMessage hubMessage)
        {
            while (await connection.Output.WaitToWriteAsync())
            {
                if (connection.Output.TryWrite(hubMessage))
                {
                    return;
                }
            }

            // Output is closed. Cancel this invocation completely
            _logger.OutboundChannelClosed();
            throw new OperationCanceledException("Outbound channel was closed while trying to write hub message");
        }

        private async Task Invoke(HubMethodDescriptor descriptor, HubConnectionContext connection, InvocationMessage invocationMessage)
        {
            var methodExecutor = descriptor.MethodExecutor;

            using (var scope = _serviceScopeFactory.CreateScope())
            {
                if (!await IsHubMethodAuthorized(scope.ServiceProvider, connection.User, descriptor.Policies))
                {
                    _logger.HubMethodNotAuthorized(invocationMessage.Target);
                    if (!invocationMessage.NonBlocking)
                    {
                        await SendMessageAsync(connection, CompletionMessage.WithError(invocationMessage.InvocationId, $"Failed to invoke '{invocationMessage.Target}' because user is unauthorized"));
                    }
                    return;
                }

                var hubActivator = scope.ServiceProvider.GetRequiredService<IHubActivator<THub>>();
                var hub = hubActivator.Create();

                try
                {
                    InitializeHub(hub, connection);

                    object result = null;

                    // ReadableChannel is awaitable but we don't want to await it.
                    if (methodExecutor.IsMethodAsync && !IsChannel(methodExecutor.MethodReturnType, out _))
                    {
                        if (methodExecutor.MethodReturnType == typeof(Task))
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

                    if (IsStreamed(connection, invocationMessage.InvocationId, methodExecutor, result, methodExecutor.MethodReturnType, out var enumerator))
                    {
                        _logger.StreamingResult(invocationMessage.InvocationId, methodExecutor.MethodReturnType.FullName);
                        await StreamResultsAsync(invocationMessage.InvocationId, connection, enumerator);
                    }
                    else if (!invocationMessage.NonBlocking)
                    {
                        _logger.SendingResult(invocationMessage.InvocationId, methodExecutor.MethodReturnType.FullName);
                        await SendMessageAsync(connection, CompletionMessage.WithResult(invocationMessage.InvocationId, result));
                    }
                }
                catch (TargetInvocationException ex)
                {
                    _logger.FailedInvokingHubMethod(invocationMessage.Target, ex);
                    await SendInvocationError(invocationMessage, connection, methodExecutor.MethodReturnType, ex.InnerException);
                }
                catch (Exception ex)
                {
                    _logger.FailedInvokingHubMethod(invocationMessage.Target, ex);
                    await SendInvocationError(invocationMessage, connection, methodExecutor.MethodReturnType, ex);
                }
                finally
                {
                    hubActivator.Release(hub);
                }
            }
        }

        private async Task SendInvocationError(InvocationMessage invocationMessage, HubConnectionContext connection, Type returnType, Exception ex)
        {
            if (!invocationMessage.NonBlocking)
            {
                if (IsIObservable(returnType) || IsChannel(returnType, out _))
                {
                    await SendMessageAsync(connection, new StreamCompletionMessage(invocationMessage.InvocationId, ex.Message));
                }
                else
                {
                    await SendMessageAsync(connection, CompletionMessage.WithError(invocationMessage.InvocationId, ex.Message));
                }
            }
        }

        private void InitializeHub(THub hub, HubConnectionContext connection)
        {
            hub.Clients = _hubContext.Clients;
            hub.Context = new HubCallerContext(connection);
            hub.Groups = _hubContext.Groups;
        }

        private bool IsChannel(Type type, out Type payloadType)
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

        private async Task StreamResultsAsync(string invocationId, HubConnectionContext connection, IAsyncEnumerator<object> enumerator)
        {
            string error = null;

            try
            {
                while (await enumerator.MoveNextAsync())
                {
                    // Send the stream item
                    await SendMessageAsync(connection, new StreamItemMessage(invocationId, enumerator.Current));
                }
            }
            catch (Exception ex)
            {
                // If the streaming method was canceled we don't want to send a HubException message - this is not an error case
                if (!(ex is OperationCanceledException && connection.ActiveRequestCancellationSources.TryGetValue(invocationId, out var cts)
                    && cts.IsCancellationRequested))
                {
                    error = ex.Message;
                }
            }
            finally
            {
                await SendMessageAsync(connection, new StreamCompletionMessage(invocationId, error: error));

                if (connection.ActiveRequestCancellationSources.TryRemove(invocationId, out var cts))
                {
                    cts.Dispose();
                }
            }
        }

        private bool IsStreamed(HubConnectionContext connection, string invocationId, ObjectMethodExecutor methodExecutor, object result, Type resultType, out IAsyncEnumerator<object> enumerator)
        {
            if (result == null)
            {
                enumerator = null;
                return false;
            }

            var observableInterface = IsIObservable(resultType) ?
                resultType :
                resultType.GetInterfaces().FirstOrDefault(IsIObservable);
            if (observableInterface != null)
            {
                enumerator = AsyncEnumeratorAdapters.FromObservable(result, observableInterface, CreateCancellation());
                return true;
            }
            else if (IsChannel(resultType, out var payloadType))
            {
                enumerator = AsyncEnumeratorAdapters.FromChannel(result, payloadType, CreateCancellation());
                return true;
            }
            else
            {
                // Not streamed
                enumerator = null;
                return false;
            }

            CancellationToken CreateCancellation()
            {
                var streamCts = new CancellationTokenSource();
                connection.ActiveRequestCancellationSources.TryAdd(invocationId, streamCts);
                return CancellationTokenSource.CreateLinkedTokenSource(connection.ConnectionAbortedToken, streamCts.Token).Token;
            }
        }

        private static bool IsIObservable(Type iface)
        {
            return iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IObservable<>);
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

                _logger.HubMethodBound(methodName);
            }
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
