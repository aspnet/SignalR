using System;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Channels;

namespace Microsoft.AspNetCore.SignalR
{
    public static class HubProxyExtensions
    {
        public static Task InvokeAsync(this IHubClientProxy hubProxy, string methodName, params object[] args) =>
            InvokeAsync(hubProxy, methodName, CancellationToken.None, args);

        public static Task InvokeAsync(this IHubClientProxy hubProxy, string methodName, CancellationToken cancellationToken, params object[] args)
        {
            if (hubProxy == null)
            {
                throw new ArgumentNullException(nameof(hubProxy));
            }

            return hubProxy.InvokeAsync(methodName, typeof(object), cancellationToken, args);
        }

        public static Task<TResult> InvokeAsync<TResult>(this IHubClientProxy hubProxy, string methodName, params object[] args) =>
            InvokeAsync<TResult>(hubProxy, methodName, CancellationToken.None, args);

        public async static Task<TResult> InvokeAsync<TResult>(this IHubClientProxy hubProxy, string methodName, CancellationToken cancellationToken, params object[] args)
        {
            if (hubProxy == null)
            {
                throw new ArgumentNullException(nameof(hubProxy));
            }

            return (TResult)await hubProxy.InvokeAsync(methodName, typeof(TResult), cancellationToken, args);
        }

        public static ReadableChannel<TResult> Stream<TResult>(this IHubClientProxy hubProxy, string methodName, params object[] args) =>
            Stream<TResult>(hubProxy, methodName, CancellationToken.None, args);

        public static ReadableChannel<TResult> Stream<TResult>(this IHubClientProxy hubProxy, string methodName, CancellationToken cancellationToken, params object[] args)
        {
            if (hubProxy == null)
            {
                throw new ArgumentNullException(nameof(hubProxy));
            }

            var inputChannel = hubProxy.Stream(methodName, typeof(TResult), cancellationToken, args);
            var outputChannel = Channel.CreateUnbounded<TResult>();

            // Local function to provide a way to run async code as fire-and-forget
            // The output channel is how we signal completion to the caller.
            async Task RunChannel()
            {
                try
                {
                    while (await inputChannel.WaitToReadAsync())
                    {
                        while (inputChannel.TryRead(out var item))
                        {
                            while (!outputChannel.Out.TryWrite((TResult)item))
                            {
                                if (!await outputChannel.Out.WaitToWriteAsync())
                                {
                                    // Failed to write to the output channel because it was closed. Nothing really we can do but abort here.
                                    return;
                                }
                            }
                        }
                    }

                    // Manifest any errors in the completion task
                    await inputChannel.Completion;
                }
                catch (Exception ex)
                {
                    outputChannel.Out.TryComplete(ex);
                }
                finally
                {
                    // This will safely no-op if the catch block above ran.
                    outputChannel.Out.TryComplete();
                }
            }

            _ = RunChannel();

            return outputChannel.In;
        }

    }
}
