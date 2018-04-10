// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics.Tracing;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Internal;

namespace Microsoft.AspNetCore.Http.Connections.Internal
{
    public class TimerAwaitable : IDisposable, ICriticalNotifyCompletion
    {
        private Timer _timer;
        private Action _callback;
        private static readonly Action _callbackCompleted = () => { };

        private TimeSpan _period;

        private TimeSpan _dueTime;
        private bool _disposed;
        private volatile bool _running;

        public TimerAwaitable(TimeSpan dueTime, TimeSpan period)
        {
            _dueTime = dueTime;
            _period = period;
        }

        public bool IsRunning => _running;

        public void Start()
        {
            if (_timer == null)
            {
                lock (this)
                {
                    if (_disposed)
                    {
                        return;
                    }

                    if (_timer == null)
                    {
                        _timer = new Timer(state => ((TimerAwaitable)state).Tick(), this, _dueTime, _period);
                    }
                }
            }
        }

        public TimerAwaitable GetAwaiter() => this;
        public bool IsCompleted => ReferenceEquals(_callback, _callbackCompleted);

        public void GetResult()
        {
            _callback = null;
        }

        private void Tick()
        {
            var continuation = Interlocked.Exchange(ref _callback, _callbackCompleted);
            continuation?.Invoke();
        }

        public void OnCompleted(Action continuation)
        {
            if (ReferenceEquals(_callback, _callbackCompleted) ||
                ReferenceEquals(Interlocked.CompareExchange(ref _callback, continuation, null), _callbackCompleted))
            {
                Task.Run(continuation);
            }
        }

        public void UnsafeOnCompleted(Action continuation)
        {
            OnCompleted(continuation);
        }

        public void Stop()
        {
            _running = false;

            Tick();
        }

        void IDisposable.Dispose()
        {
            lock (this)
            {
                _disposed = true;

                _timer?.Dispose();

                _timer = null;
            }
        }
    }
}