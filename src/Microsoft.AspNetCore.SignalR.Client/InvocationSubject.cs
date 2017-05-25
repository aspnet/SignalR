// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Microsoft.AspNetCore.SignalR.Client
{
    internal class InvocationSubject : IObservable<object>
    {
        private readonly object _lock = new object();
        private List<IObserver<object>> _observers = new List<IObserver<object>>();
        private Exception _error = null;
        private bool _completed = false;

        public IDisposable Subscribe(IObserver<object> observer)
        {
            // We lock here so that nobody can trigger completed/error
            // until we've added our observer (if we're going to)
            // If we're already completed/faulted, we can continue outside
            // the lock safely because nobody can "uncomplete" or "unfault" us.
            lock (_lock)
            {
                if (!_completed && _error == null)
                {
                    _observers.Add(observer);
                    return new Subscription(this, observer);
                }
            }

            if(_error != null)
            {
                observer.OnError(_error);
            }
            else
            {
                Debug.Assert(_completed, "Expected that if the subject wasn't faulted, it was completed.");
                observer.OnCompleted();
            }
            return Subscription.Null;
        }

        public bool TryOnCompleted()
        {
            List<IObserver<object>> localObservers;
            lock (_lock)
            {
                if (_completed || _error != null)
                {
                    return false;
                }

                localObservers = new List<IObserver<object>>(_observers);
                _completed = true;
                _observers.Clear();
            }

            foreach (var observer in localObservers)
            {
                observer.OnCompleted();
            }
            return true;
        }

        public bool TryOnError(Exception error)
        {
            List<IObserver<object>> localObservers;
            lock (_lock)
            {
                if (_completed || _error != null)
                {
                    return false;
                }

                localObservers = new List<IObserver<object>>(_observers);
                _error = error;
                _observers.Clear();
            }

            foreach (var observer in localObservers)
            {
                observer.OnError(error);
            }
            return true;
        }

        public bool TryOnNext(object value)
        {
            List<IObserver<object>> localObservers;
            lock (_lock)
            {
                if (_completed || _error != null)
                {
                    return false;
                }

                localObservers = new List<IObserver<object>>(_observers);
            }

            foreach (var observer in localObservers)
            {
                observer.OnNext(value);
            }
            return true;
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
            public static readonly Subscription Null = new Subscription(null, null);

            private InvocationSubject _invocationObservable;
            private IObserver<object> _observer;

            public Subscription(InvocationSubject invocationObservable, IObserver<object> observer)
            {
                _invocationObservable = invocationObservable;
                _observer = observer;
            }

            public void Dispose()
            {
                if (_invocationObservable != null && _observer != null)
                {
                    _invocationObservable.Unsubscribe(_observer);
                }
            }
        }
    }
}
