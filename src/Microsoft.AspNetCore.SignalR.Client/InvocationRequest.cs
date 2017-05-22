// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.SignalR.Client
{
    internal class InvocationRequest : IDisposable
    {
        private readonly TaskCompletionSource<object> _completionSource;
        private readonly CancellationTokenRegistration _cancellationTokenRegistration;
        private readonly bool _streaming;
        private readonly ILogger _logger;
        private readonly InvocationSubject _subject;

        public Type ResultType { get; }
        public CancellationToken CancellationToken { get; }
        public string InvocationId { get; }

        public Task<object> Task => _completionSource?.Task;
        public IObservable<object> Observable => _subject;

        public InvocationRequest(CancellationToken cancellationToken, Type resultType, string invocationId, ILoggerFactory loggerFactory, bool streaming)
        {
            _logger = loggerFactory.CreateLogger<InvocationRequest>();
            _cancellationTokenRegistration = cancellationToken.Register(() => _completionSource.TrySetCanceled());
            _streaming = streaming;

            if (_streaming)
            {
                _subject = new InvocationSubject();
            }
            else
            {
                _completionSource = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            InvocationId = invocationId;
            CancellationToken = cancellationToken;
            ResultType = resultType;

            _logger.LogTrace("Invocation {invocationId} created", InvocationId);
        }

        public void Fail(Exception exception)
        {
            _logger.LogTrace("Invocation {invocationId} marked as failed.", InvocationId);
            if (_streaming)
            {
                _subject.TryOnError(exception);
            }
            else
            {
                _completionSource.TrySetException(exception);
            }
        }

        public void Complete(object result)
        {
            _logger.LogTrace("Invocation {invocationId} marked as completed.", InvocationId);
            if (_streaming)
            {
                if (result != null)
                {
                    _logger.LogError("Invocation {invocationId} received a completion result, but was invoked as a streaming invocation.", InvocationId);
                    _subject.TryOnError(new InvalidOperationException("Server provided a result in a completion response to a streamed invocation."));
                }
                else
                {
                    _subject.TryOnCompleted();
                }
            }
            else
            {
                _completionSource.TrySetResult(result);
            }
        }

        public void StreamItem(object item)
        {
            if(_streaming)
            {
                _logger.LogTrace("Invocation {invocationId} received stream item.", InvocationId);
                _subject.TryOnNext(item);
            }
            else
            {
                _logger.LogError("Invocation {invocationId} received stream item but was invoked as a non-streamed invocation.", InvocationId);
                _completionSource.TrySetException(new InvalidOperationException("Streaming methods must be invoked using HubConnection.Stream"));
            }
        }

        public void Dispose()
        {
            _logger.LogTrace("Invocation {invocationId} disposed", InvocationId);

            // Just in case it hasn't already been completed
            if (_streaming)
            {
                _subject.TryOnError(new OperationCanceledException("Connection terminated"));
            }
            else
            {
                _completionSource.TrySetCanceled();
            }

            _cancellationTokenRegistration.Dispose();
        }
    }
}
