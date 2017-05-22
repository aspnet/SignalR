// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;

namespace Microsoft.AspNetCore.SignalR.Client
{
    internal class InvocationSubject : IObservable<object>
    {
        private readonly object _lock = new object();
        private IList<IObserver<object>> _observers = new List<IObserver<object>>();
        private Exception _error = null;
        private bool _completed = false;

        public IDisposable Subscribe(IObserver<object> observer)
        {
            lock (_lock)
            {
                if (_completed)
                {
                    observer.OnCompleted();
                }
                else if (_error != null)
                {
                    observer.OnError(_error);
                }
                else
                {
                    _observers.Add(observer);
                }
                return new Subscription(this, observer);
            }
        }

        public bool TryOnCompleted()
        {
            lock (_lock)
            {
                if (_completed || _error != null)
                {
                    return false;
                }

                _completed = true;
                foreach (var observer in _observers)
                {
                    observer.OnCompleted();
                }
                _observers.Clear();
                return true;
            }
        }

        public bool TryOnError(Exception error)
        {
            lock (_lock)
            {
                if (_completed || _error != null)
                {
                    return false;
                }

                _error = error;
                foreach (var observer in _observers)
                {
                    observer.OnError(error);
                }
                _observers.Clear();
                return true;
            }
        }

        public bool TryOnNext(object value)
        {
            lock (_lock)
            {
                if (_completed || _error != null)
                {
                    return false;
                }

                foreach (var observer in _observers)
                {
                    observer.OnNext(value);
                }
                return true;
            }
        }

        private void Unsubscribe(IObserver<object> observer)
        {
            lock (_lock)
            {
                _observers.Remove(observer);
            }
        }

        private class Subscription : IDisposable
        {
            private InvocationSubject _invocationObservable;
            private IObserver<object> _observer;

            public Subscription(InvocationSubject invocationObservable, IObserver<object> observer)
            {
                _invocationObservable = invocationObservable;
                _observer = observer;
            }

            public void Dispose()
            {
                _invocationObservable.Unsubscribe(_observer);
            }
        }
    }
}
