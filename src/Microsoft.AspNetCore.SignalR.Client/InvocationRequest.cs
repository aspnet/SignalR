// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.SignalR.Client
{
    internal abstract class InvocationRequest : IDisposable
    {
        private readonly CancellationTokenRegistration _cancellationTokenRegistration;
        private readonly bool _streaming;

        protected ILogger Logger { get; }

        public Type ResultType { get; }
        public CancellationToken CancellationToken { get; }
        public string InvocationId { get; }

        protected InvocationRequest(CancellationToken cancellationToken, Type resultType, string invocationId, ILogger logger)
        {
            _cancellationTokenRegistration = cancellationToken.Register(self => ((InvocationRequest)self).Cancel(), this);

            InvocationId = invocationId;
            CancellationToken = cancellationToken;
            ResultType = resultType;

            Logger.LogTrace("Invocation {invocationId} created", InvocationId);
        }

        public static InvocationRequest Invoke(CancellationToken cancellationToken, Type resultType, string invocationId, ILoggerFactory loggerFactory, out Task<object> result)
        {
            var req = new NonStreaming(cancellationToken, resultType, invocationId, loggerFactory);
            result = req.Result;
            return req;
        }


        public static InvocationRequest Stream(CancellationToken cancellationToken, Type resultType, string invocationId, ILoggerFactory loggerFactory, out IObservable<object> result)
        {
            var req = new Streaming(cancellationToken, resultType, invocationId, loggerFactory);
            result = req.Result;
            return req;
        }

        public abstract void Fail(Exception exception);
        public abstract void Complete(object result);
        public abstract void StreamItem(object item);

        protected abstract void Cancel();

        public virtual void Dispose()
        {
            Logger.LogTrace("Invocation {invocationId} disposed", InvocationId);

            // Just in case it hasn't already been completed
            Cancel();

            _cancellationTokenRegistration.Dispose();
        }

        private class Streaming : InvocationRequest
        {
            private readonly InvocationSubject _subject;

            public Streaming(CancellationToken cancellationToken, Type resultType, string invocationId, ILoggerFactory loggerFactory)
                : base(cancellationToken, resultType, invocationId, loggerFactory.CreateLogger<Streaming>())
            {
            }

            public IObservable<object> Result => _subject;

            public override void Complete(object result)
            {
                Logger.LogTrace("Invocation {invocationId} marked as completed.", InvocationId);
                if (result != null)
                {
                    Logger.LogError("Invocation {invocationId} received a completion result, but was invoked as a streaming invocation.", InvocationId);
                    _subject.TryOnError(new InvalidOperationException("Server provided a result in a completion response to a streamed invocation."));
                }
                else
                {
                    _subject.TryOnCompleted();
                }
            }

            public override void Fail(Exception exception)
            {
                Logger.LogTrace("Invocation {invocationId} marked as failed.", InvocationId);
                _subject.TryOnError(exception);
            }

            public override void StreamItem(object item)
            {
                Logger.LogTrace("Invocation {invocationId} received stream item.", InvocationId);
                _subject.TryOnNext(item);
            }

            protected override void Cancel()
            {
                _subject.TryOnError(new OperationCanceledException("Connection terminated"));
            }
        }

        private class NonStreaming : InvocationRequest
        {
            private readonly TaskCompletionSource<object> _completionSource = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);

            public NonStreaming(CancellationToken cancellationToken, Type resultType, string invocationId, ILoggerFactory loggerFactory)
                : base(cancellationToken, resultType, invocationId, loggerFactory.CreateLogger<NonStreaming>())
            {
            }

            public Task<object> Result => _completionSource.Task;

            public override void Complete(object result)
            {
                Logger.LogTrace("Invocation {invocationId} marked as completed.", InvocationId);
                _completionSource.TrySetResult(result);
            }

            public override void Fail(Exception exception)
            {
                Logger.LogTrace("Invocation {invocationId} marked as failed.", InvocationId);
                _completionSource.TrySetException(exception);
            }

            public override void StreamItem(object item)
            {
                Logger.LogError("Invocation {invocationId} received stream item but was invoked as a non-streamed invocation.", InvocationId);
                _completionSource.TrySetException(new InvalidOperationException("Streaming methods must be invoked using HubConnection.Stream"));
            }

            protected override void Cancel()
            {
                _completionSource.TrySetCanceled();
            }
        }
    }
}
