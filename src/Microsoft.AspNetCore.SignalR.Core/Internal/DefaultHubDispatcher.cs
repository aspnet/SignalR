// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.AspNetCore.SignalR.Internal
{
    public partial class DefaultHubDispatcher<THub> : HubDispatcher<THub> where THub : Hub
    {
        private readonly ChannelStore _channelStore = new ChannelStore();

        private readonly Dictionary<string, HubMethodDescriptor> _methods = new Dictionary<string, HubMethodDescriptor>(StringComparer.OrdinalIgnoreCase);
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly IHubContext<THub> _hubContext;
        private readonly ILogger<HubDispatcher<THub>> _logger;
        private readonly bool _enableDetailedErrors;
        public DefaultHubDispatcher(IServiceScopeFactory serviceScopeFactory, IHubContext<THub> hubContext, IOptions<HubOptions<THub>> hubOptions,
            IOptions<HubOptions> globalHubOptions, ILogger<DefaultHubDispatcher<THub>> logger)
        {
            _serviceScopeFactory = serviceScopeFactory;
            _hubContext = hubContext;
            _enableDetailedErrors = hubOptions.Value.EnableDetailedErrors ?? globalHubOptions.Value.EnableDetailedErrors ?? false;
            _logger = logger;
            DiscoverHubMethods();
        }

        public override async Task OnConnectedAsync(HubConnectionContext connection)
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

        public override async Task OnDisconnectedAsync(HubConnectionContext connection, Exception exception)
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

        public override Task DispatchMessageAsync(HubConnectionContext connection, HubMessage hubMessage)
        {
            // Messages are dispatched sequentially and will stop other messages from being processed until they complete.
            // Streaming methods will run sequentially until they start streaming, then they will fire-and-forget allowing other messages to run.

            switch (hubMessage)
            {
                case InvocationBindingFailureMessage bindingFailureMessage:
                    return ProcessBindingFailure(connection, bindingFailureMessage);

                case InvocationMessage invocationMessage:
                    Log.ReceivedHubInvocation(_logger, invocationMessage);
                    return ProcessInvocation(connection, invocationMessage, isStreamResponse: false, isStreamCall: invocationMessage.StreamingUpload);

                case StreamInvocationMessage streamInvocationMessage:
                    Log.ReceivedStreamHubInvocation(_logger, streamInvocationMessage);
                    return ProcessInvocation(connection, streamInvocationMessage, isStreamResponse: true, isStreamCall: false);

                case CancelInvocationMessage cancelInvocationMessage:
                    // Check if there is an associated active stream and cancel it if it exists.
                    // The cts will be removed when the streaming method completes executing
                    if (connection.ActiveRequestCancellationSources.TryGetValue(cancelInvocationMessage.InvocationId, out var cts))
                    {
                        Log.CancelStream(_logger, cancelInvocationMessage.InvocationId);
                        cts.Cancel();
                    }
                    else
                    {
                        // Stream can be canceled on the server while client is canceling stream.
                        Log.UnexpectedCancel(_logger);
                    }
                    break;

                case PingMessage _:
                    // We don't care about pings
                    break;

                case StreamItemMessage streamItem:
                    return ProcessStreamItem(connection, streamItem);

                case StreamCompleteMessage streamComplete:
                    return ProcessStreamComplete(connection, streamComplete);

                // Other kind of message we weren't expecting
                default:
                    Log.UnsupportedMessageReceived(_logger, hubMessage.GetType().FullName);
                    throw new NotSupportedException($"Received unsupported message: {hubMessage}");
            }

            return Task.CompletedTask;
        }

        private Task ProcessBindingFailure(HubConnectionContext connection, InvocationBindingFailureMessage bindingFailureMessage)
        {
            Log.FailedInvokingHubMethod(_logger, bindingFailureMessage.Target, bindingFailureMessage.BindingFailure.SourceException);
            var errorMessage = ErrorMessageHelper.BuildErrorMessage($"Failed to invoke '{bindingFailureMessage.Target}' due to an error on the server.",
                bindingFailureMessage.BindingFailure.SourceException, _enableDetailedErrors);
            return SendInvocationError(bindingFailureMessage.InvocationId, connection, errorMessage);
        }
        public override IReadOnlyList<Type> GetParameterTypes(string methodName)
        {
            if (!_methods.TryGetValue(methodName, out var descriptor))
            {
                return Type.EmptyTypes;
            }
            return descriptor.ParameterTypes;
        }

        private async Task ProcessStreamItem(HubConnectionContext connection, StreamItemMessage message)
        {
            Debug.WriteLine($"item: id={message.InvocationId} data={message.Item}");
            await _channelStore.ProcessItem(message);
        }

        private async Task ProcessStreamComplete(HubConnectionContext connection, StreamCompleteMessage message)
        {
            // closes channels, removes from Lookup dict
            _channelStore.Complete(message);

            await Task.Delay(100); 

            // close the associated hub
            // gonna need to cancel some tokens probably
        }

        private Task ProcessInvocation(HubConnectionContext connection,
            HubMethodInvocationMessage hubMethodInvocationMessage, bool isStreamResponse, bool isStreamCall)
        {
            if (!_methods.TryGetValue(hubMethodInvocationMessage.Target, out var descriptor))
            {
                // Send an error to the client. Then let the normal completion process occur
                Log.UnknownHubMethod(_logger, hubMethodInvocationMessage.Target);
                return connection.WriteAsync(CompletionMessage.WithError(
                    hubMethodInvocationMessage.InvocationId, $"Unknown hub method '{hubMethodInvocationMessage.Target}'")).AsTask();
            }
            if (isStreamResponse && isStreamCall)
            {
                throw new Exception("Woah Slow Down There Partner Ya Can't Stream BOTH Ways");
            }
            else
            {
                return Invoke(descriptor, connection, hubMethodInvocationMessage, isStreamResponse, isStreamCall);
            }
        }

        private async Task Invoke(HubMethodDescriptor descriptor, HubConnectionContext connection,
            HubMethodInvocationMessage hubMethodInvocationMessage, bool isStreamResponse, bool isStreamCall)
        {
            var methodExecutor = descriptor.MethodExecutor;

            var disposeScope = true;
            var scope = _serviceScopeFactory.CreateScope();
            IHubActivator<THub> hubActivator = null;
            THub hub = null;
            try
            {
                if (!await IsHubMethodAuthorized(scope.ServiceProvider, connection.User, descriptor.Policies))
                {
                    Log.HubMethodNotAuthorized(_logger, hubMethodInvocationMessage.Target);
                    await SendInvocationError(hubMethodInvocationMessage.InvocationId, connection,
                        $"Failed to invoke '{hubMethodInvocationMessage.Target}' because user is unauthorized");
                    return;
                }

                if (!await ValidateInvocationMode(descriptor, isStreamResponse, hubMethodInvocationMessage, connection))
                {
                    return;
                }

                hubActivator = scope.ServiceProvider.GetRequiredService<IHubActivator<THub>>();
                hub = hubActivator.Create();

                try
                {
                    InitializeHub(hub, connection);

                    // TODO :: refactor this flow control lmao
                    if (isStreamCall)
                    {
                        disposeScope = false;

                        // need to make a pipe
                        // and pass one end of the pipe to the proper method on the hub

                        var placeholder = hubMethodInvocationMessage.Arguments[0] as ChannelPlaceholder;
                        if (placeholder == null)
                        {
                            // Sneaky Plumbing ::
                            //   currently the idea is
                            //   the client sees that you pass in a channel listener
                            //   and replaces that with the string "placeholder"
                            //   and then sends that string over
                            //   so if it's a valid upload stream, there should be this string where the listener originally was
                            //   and we replace it with a new pipe on the server side, and send that to the hub
                            throw new Exception("invalid arguments for a streaming upload.");
                        }

                        _channelStore.AddStream(hubMethodInvocationMessage.InvocationId, placeholder.ItemType);

                        // reader goes here, used to invoke the hub's method
                        hubMethodInvocationMessage.Arguments[0] = _channelStore.Lookup[hubMethodInvocationMessage.InvocationId].Reader;

                        // spin off that boy in a background thread
                        // it waits until the hubMethod finishes and returns the result
                        async Task ExecuteStreamingUpload()
                        {
                            var result = await ExecuteHubMethod(methodExecutor, hub, hubMethodInvocationMessage.Arguments);
                            Log.SendingResult(_logger, hubMethodInvocationMessage.InvocationId, methodExecutor);
                            await connection.WriteAsync(CompletionMessage.WithResult(hubMethodInvocationMessage.InvocationId, result));
                        }
                        _ = ExecuteStreamingUpload();
                    }

                    else
                    {
                        var result = await ExecuteHubMethod(methodExecutor, hub, hubMethodInvocationMessage.Arguments);

                        if (isStreamResponse)
                        {
                            if (!TryGetStreamingEnumerator(connection, hubMethodInvocationMessage.InvocationId, descriptor, result, out var enumerator, out var streamCts))
                            {
                                Log.InvalidReturnValueFromStreamingMethod(_logger, methodExecutor.MethodInfo.Name);

                                await SendInvocationError(hubMethodInvocationMessage.InvocationId, connection,
                                    $"The value returned by the streaming method '{methodExecutor.MethodInfo.Name}' is not a ChannelReader<>.");
                                return;
                            }

                            disposeScope = false;
                            Log.StreamingResult(_logger, hubMethodInvocationMessage.InvocationId, methodExecutor);
                            // Fire-and-forget stream invocations, otherwise they would block other hub invocations from being able to run
                            _ = StreamResultsAsync(hubMethodInvocationMessage.InvocationId, connection, enumerator, scope, hubActivator, hub, streamCts);
                        }
                        // Non-empty/null InvocationId ==> Blocking invocation that needs a response
                        else if (!string.IsNullOrEmpty(hubMethodInvocationMessage.InvocationId))
                        {
                            Log.SendingResult(_logger, hubMethodInvocationMessage.InvocationId, methodExecutor);
                            await connection.WriteAsync(CompletionMessage.WithResult(hubMethodInvocationMessage.InvocationId, result));
                        }
                    }
                }
                catch (TargetInvocationException ex)
                {
                    Log.FailedInvokingHubMethod(_logger, hubMethodInvocationMessage.Target, ex);
                    await SendInvocationError(hubMethodInvocationMessage.InvocationId, connection,
                        ErrorMessageHelper.BuildErrorMessage($"An unexpected error occurred invoking '{hubMethodInvocationMessage.Target}' on the server.", ex.InnerException, _enableDetailedErrors));
                }
                catch (Exception ex)
                {
                    Log.FailedInvokingHubMethod(_logger, hubMethodInvocationMessage.Target, ex);
                    await SendInvocationError(hubMethodInvocationMessage.InvocationId, connection,
                        ErrorMessageHelper.BuildErrorMessage($"An unexpected error occurred invoking '{hubMethodInvocationMessage.Target}' on the server.", ex, _enableDetailedErrors));
                }
            }
            finally
            {
                if (disposeScope)
                {
                    hubActivator?.Release(hub);
                    scope.Dispose();
                }
            }
        }

        private async Task StreamResultsAsync(string invocationId, HubConnectionContext connection, IAsyncEnumerator<object> enumerator, IServiceScope scope,
            IHubActivator<THub> hubActivator, THub hub, CancellationTokenSource streamCts)
        {
            string error = null;

            using (scope)
            {
                try
                {
                    while (await enumerator.MoveNextAsync())
                    {
                        // Send the stream item
                        await connection.WriteAsync(new StreamItemMessage(invocationId, enumerator.Current));
                    }
                }
                catch (ChannelClosedException ex)
                {
                    // If the channel closes from an exception in the streaming method, grab the innerException for the error from the streaming method
                    error = ErrorMessageHelper.BuildErrorMessage("An error occurred on the server while streaming results.", ex.InnerException ?? ex, _enableDetailedErrors);
                }
                catch (Exception ex)
                {
                    // If the streaming method was canceled we don't want to send a HubException message - this is not an error case
                    if (!(ex is OperationCanceledException && connection.ActiveRequestCancellationSources.TryGetValue(invocationId, out var cts)
                        && cts.IsCancellationRequested))
                    {
                        error = ErrorMessageHelper.BuildErrorMessage("An error occurred on the server while streaming results.", ex, _enableDetailedErrors);
                    }
                }
                finally
                {
                    (enumerator as IDisposable)?.Dispose();

                    hubActivator.Release(hub);

                    // Dispose the linked CTS for the stream.
                    streamCts.Dispose();

                    await connection.WriteAsync(CompletionMessage.WithError(invocationId, error));

                    if (connection.ActiveRequestCancellationSources.TryRemove(invocationId, out var cts))
                    {
                        cts.Dispose();
                    }
                }
            }
        }
        private static async Task<object> ExecuteHubMethod(ObjectMethodExecutor methodExecutor, THub hub, object[] arguments)
        {
            if (methodExecutor.IsMethodAsync)
            {
                if (methodExecutor.MethodReturnType == typeof(Task))
                {
                    await (Task)methodExecutor.Execute(hub, arguments);
                }
                else
                {
                    return await methodExecutor.ExecuteAsync(hub, arguments);
                }
            }
            else
            {
                return methodExecutor.Execute(hub, arguments);
            }

            return null;
        }

        private async Task SendInvocationError(string invocationId,
            HubConnectionContext connection, string errorMessage)
        {
            if (string.IsNullOrEmpty(invocationId))
            {
                return;
            }

            await connection.WriteAsync(CompletionMessage.WithError(invocationId, errorMessage));
        }

        private void InitializeHub(THub hub, HubConnectionContext connection)
        {
            hub.Clients = new HubCallerClients(_hubContext.Clients, connection.ConnectionId);
            hub.Context = new DefaultHubCallerContext(connection);
            hub.Groups = _hubContext.Groups;
        }

        private Task<bool> IsHubMethodAuthorized(IServiceProvider provider, ClaimsPrincipal principal, IList<IAuthorizeData> policies)
        {
            // If there are no policies we don't need to run auth
            if (!policies.Any())
            {
                return TaskCache.True;
            }

            return IsHubMethodAuthorizedSlow(provider, principal, policies);
        }

        private static async Task<bool> IsHubMethodAuthorizedSlow(IServiceProvider provider, ClaimsPrincipal principal, IList<IAuthorizeData> policies)
        {
            var authService = provider.GetRequiredService<IAuthorizationService>();
            var policyProvider = provider.GetRequiredService<IAuthorizationPolicyProvider>();

            var authorizePolicy = await AuthorizationPolicy.CombineAsync(policyProvider, policies);
            // AuthorizationPolicy.CombineAsync only returns null if there are no policies and we check that above
            Debug.Assert(authorizePolicy != null);

            var authorizationResult = await authService.AuthorizeAsync(principal, authorizePolicy);
            // Only check authorization success, challenge or forbid wouldn't make sense from a hub method invocation
            return authorizationResult.Succeeded;
        }

        private async Task<bool> ValidateInvocationMode(HubMethodDescriptor hubMethodDescriptor, bool isStreamedInvocation,
            HubMethodInvocationMessage hubMethodInvocationMessage, HubConnectionContext connection)
        {
            if (hubMethodDescriptor.IsStreamable && !isStreamedInvocation)
            {
                // Non-null/empty InvocationId? Blocking
                if (!string.IsNullOrEmpty(hubMethodInvocationMessage.InvocationId))
                {
                    Log.StreamingMethodCalledWithInvoke(_logger, hubMethodInvocationMessage);
                    await connection.WriteAsync(CompletionMessage.WithError(hubMethodInvocationMessage.InvocationId,
                        $"The client attempted to invoke the streaming '{hubMethodInvocationMessage.Target}' method with a non-streaming invocation."));
                }

                return false;
            }

            if (!hubMethodDescriptor.IsStreamable && isStreamedInvocation)
            {
                Log.NonStreamingMethodCalledWithStream(_logger, hubMethodInvocationMessage);
                await connection.WriteAsync(CompletionMessage.WithError(hubMethodInvocationMessage.InvocationId,
                    $"The client attempted to invoke the non-streaming '{hubMethodInvocationMessage.Target}' method with a streaming invocation."));

                return false;
            }

            return true;
        }

        private bool TryGetStreamingEnumerator(HubConnectionContext connection, string invocationId, HubMethodDescriptor hubMethodDescriptor, object result, out IAsyncEnumerator<object> enumerator, out CancellationTokenSource streamCts)
        {
            if (result != null)
            {
                if (hubMethodDescriptor.IsChannel)
                {
                    streamCts = CreateCancellation();
                    enumerator = hubMethodDescriptor.FromChannel(result, streamCts.Token);
                    return true;
                }
            }

            streamCts = null;
            enumerator = null;
            return false;

            CancellationTokenSource CreateCancellation()
            {
                var userCts = new CancellationTokenSource();
                connection.ActiveRequestCancellationSources.TryAdd(invocationId, userCts);

                return CancellationTokenSource.CreateLinkedTokenSource(connection.ConnectionAborted, userCts.Token);
            }
        }

        private void DiscoverHubMethods()
        {
            var hubType = typeof(THub);
            var hubTypeInfo = hubType.GetTypeInfo();
            var hubName = hubType.Name;

            foreach (var methodInfo in HubReflectionHelper.GetHubMethods(hubType))
            {
                var methodName =
                    methodInfo.GetCustomAttribute<HubMethodNameAttribute>()?.Name ??
                    methodInfo.Name;

                if (_methods.ContainsKey(methodName))
                {
                    throw new NotSupportedException($"Duplicate definitions of '{methodName}'. Overloading is not supported.");
                }

                var executor = ObjectMethodExecutor.Create(methodInfo, hubTypeInfo);
                var authorizeAttributes = methodInfo.GetCustomAttributes<AuthorizeAttribute>(inherit: true);
                _methods[methodName] = new HubMethodDescriptor(executor, authorizeAttributes);

                Log.HubMethodBound(_logger, hubName, methodName);
            }
        }

        public override Type GetReturnType(string invocationId)
        {
            return _channelStore.Lookup[invocationId].ReturnType;
        }
    }

    // container class instead of static

    public class ChannelStore
    {
        public Dictionary<string, ChannelConverter> Lookup = new Dictionary<string, ChannelConverter>();

        public ChannelStore()
        {

        }

        public void AddStream(string invocationId, Type itemType)
        {
            Lookup[invocationId] = new ChannelConverter(itemType);
        }   

        public async Task ProcessItem(StreamItemMessage message)
        {
            await Lookup[message.InvocationId].Writer.WriteAsync(message.Item);
        }

        public void Complete(StreamCompleteMessage message)
        {
            var ConverterToClose = Lookup[message.InvocationId];
            Lookup.Remove(message.InvocationId);

            ConverterToClose.Writer.TryComplete();
             
        }
    }

    public class ChannelConverter
    {
        private readonly static MethodInfo _createUnboundedMethod = typeof(Channel).GetMethod("CreateUnbounded", BindingFlags.Public | BindingFlags.Static, Type.DefaultBinder, Type.EmptyTypes, Array.Empty<ParameterModifier>());

        // TODO: Consider cleaning this up
        private readonly static MethodInfo _convertChannelMethod = typeof(ChannelConverter).GetMethods().Single(m => m.Name.Equals("GetGenericChannel"));

        public readonly Type ReturnType;

        // write incoming messages here
        public ChannelWriter<object> Writer { get;  }


        // pass this to the hub method
        public object Reader { get; }


        public ChannelConverter(Type itemType)
        {
            ReturnType = itemType;

            // create a new channel of type T
            var channelToHub = _createUnboundedMethod
                .MakeGenericMethod(itemType)
                .Invoke(null, Array.Empty<object>());

            Reader = channelToHub.GetType().GetProperty("Reader").GetValue(channelToHub);
            var writerToHub = channelToHub.GetType().GetProperty("Writer").GetValue(channelToHub);
            Writer = (ChannelWriter<object>)_convertChannelMethod.MakeGenericMethod(itemType).Invoke(this, new[] { writerToHub });
        }

        public ChannelWriter<object> GetGenericChannel<T>(ChannelWriter<T> writerToHub)
        {
            var genericChannel = Channel.CreateUnbounded<object>();

            _ = LaunchRelayLoop(genericChannel.Reader, writerToHub);

            return genericChannel.Writer;
        }
        
        public async Task LaunchRelayLoop<T>(ChannelReader<object> readerFromClient, ChannelWriter<T> writerToHub)
        {
            // incoming messages from the client can be read from Reader
            // cast them to type T
            // and send them the writerT
            while (await readerFromClient.WaitToReadAsync())
            {
                while (readerFromClient.TryRead(out var item))
                {
                    await writerToHub.WriteAsync((T)item);
                }
            }

            writerToHub.TryComplete();
        }
    }
}
