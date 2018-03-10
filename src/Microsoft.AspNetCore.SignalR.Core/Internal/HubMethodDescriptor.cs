// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Channels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Internal;

namespace Microsoft.AspNetCore.SignalR.Internal
{
    internal class HubMethodDescriptor
    {
        private static readonly MethodInfo FromObservableMethod = typeof(AsyncEnumeratorAdapters)
            .GetRuntimeMethods()
            .Single(m => m.Name.Equals(nameof(AsyncEnumeratorAdapters.FromObservable)) && m.IsGenericMethod);

        private static readonly MethodInfo GetAsyncEnumeratorMethod = typeof(AsyncEnumeratorAdapters)
            .GetRuntimeMethods()
            .Single(m => m.Name.Equals(nameof(AsyncEnumeratorAdapters.GetAsyncEnumerator)) && m.IsGenericMethod);

        public HubMethodDescriptor(ObjectMethodExecutor methodExecutor, IEnumerable<IAuthorizeData> policies)
        {
            MethodExecutor = methodExecutor;
            ParameterTypes = methodExecutor.MethodParameters.Select(p => p.ParameterType).ToArray();
            Policies = policies.ToArray();

            NonAsyncReturnType = (MethodExecutor.IsMethodAsync)
                ? MethodExecutor.AsyncResultType
                : MethodExecutor.MethodReturnType;

            if (IsObservableType(NonAsyncReturnType, out var observableItemType))
            {
                IsObservable = true;
                StreamReturnType = observableItemType;
            }
            else if (IsChannelType(NonAsyncReturnType, out var channelItemType))
            {
                IsChannel = true;
                StreamReturnType = channelItemType;
            }
        }

        private Func<object, CancellationToken, IAsyncEnumerator<object>> _convertToEnumerator;

        public ObjectMethodExecutor MethodExecutor { get; }

        public IReadOnlyList<Type> ParameterTypes { get; }

        public Type NonAsyncReturnType { get; }

        public bool IsObservable { get; }

        public bool IsChannel { get; }

        public bool IsStreamable => IsObservable || IsChannel;

        public Type StreamReturnType { get; }

        public IList<IAuthorizeData> Policies { get; }

        private static bool IsChannelType(Type type, out Type payloadType)
        {
            var channelType = type.AllBaseTypes().FirstOrDefault(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(ChannelReader<>));
            if (channelType == null)
            {
                payloadType = null;
                return false;
            }

            payloadType = channelType.GetGenericArguments()[0];
            return true;
        }

        private static bool IsObservableType(Type type, out Type payloadType)
        {
            var observableInterface = IsIObservable(type) ? type : type.GetInterfaces().FirstOrDefault(IsIObservable);
            if (observableInterface == null)
            {
                payloadType = null;
                return false;
            }

            payloadType = observableInterface.GetGenericArguments()[0];
            return true;

            bool IsIObservable(Type iface)
            {
                return iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IObservable<>);
            }
        }

        public IAsyncEnumerator<object> FromObservable(object observable, CancellationToken cancellationToken)
        {
            if (_convertToEnumerator == null)
            {
                _convertToEnumerator = CompileConvertToEnumerator(FromObservableMethod);
            }

            return _convertToEnumerator.Invoke(observable, cancellationToken);
        }

        public IAsyncEnumerator<object> FromChannel(object channel, CancellationToken cancellationToken)
        {
            if (_convertToEnumerator == null)
            {
                _convertToEnumerator = CompileConvertToEnumerator(GetAsyncEnumeratorMethod);
            }

            return _convertToEnumerator.Invoke(channel, cancellationToken);
        }

        private Func<object, CancellationToken, IAsyncEnumerator<object>> CompileConvertToEnumerator(MethodInfo methodInfo)
        {
            // This will call one of two methods to wrap the passed in streamable value
            // and cancellation token to an IAsyncEnumerator<object>
            //
            // IObservable<T>:
            // AsyncEnumeratorAdapters.FromObservable<T>(observable, cancellationToken);
            //
            // ChannelReader<T>
            // AsyncEnumeratorAdapters.GetAsyncEnumerator<T>(channelReader, cancellationToken);

            var genericMethodInfo = methodInfo.MakeGenericMethod(StreamReturnType);

            var methodParameters = genericMethodInfo.GetParameters();

            var targetParameter = Expression.Parameter(typeof(object), "arg1");
            var parametersParameter = Expression.Parameter(typeof(CancellationToken), "arg2");

            var parameters = new List<Expression>
            {
                Expression.Convert(targetParameter, methodParameters[0].ParameterType),
                parametersParameter
            };

            var methodCall = Expression.Call(null, genericMethodInfo, parameters);

            var castMethodCall = Expression.Convert(methodCall, typeof(IAsyncEnumerator<object>));

            var lambda = Expression.Lambda<Func<object, CancellationToken, IAsyncEnumerator<object>>>(castMethodCall, targetParameter, parametersParameter);
            return lambda.Compile();
        }
    }
}