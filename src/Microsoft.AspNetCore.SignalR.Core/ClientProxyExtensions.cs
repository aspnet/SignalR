// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.SignalR
{
    /// <summary>
    /// Extension methods for <see cref="IClientProxy"/>.
    /// </summary>
    public static class ClientProxyExtensions
    {
        /// <summary>
        /// Invokes a method on the connection(s) represented by the <see cref="IClientProxy"/> instance.
        /// Does not wait for a response from the receiver.
        /// </summary>
        /// <param name="clientProxy">The <see cref="IClientProxy"/></param>
        /// <param name="method">The name of the method to invoke.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.</param>
        /// <returns>A <see cref="Task"/> that represents the asynchronous invoke.</returns>
        public static Task SendAsync(this IClientProxy clientProxy, string method, CancellationToken cancellationToken = default)
        {
            return clientProxy.SendCoreAsync(method, Array.Empty<object>(), cancellationToken);
        }

        /// <summary>
        /// Invokes a method on the connection(s) represented by the <see cref="IClientProxy"/> instance.
        /// Does not wait for a response from the receiver.
        /// </summary>
        /// <param name="clientProxy">The <see cref="IClientProxy"/></param>
        /// <param name="method">The name of the method to invoke.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.</param>
        /// <returns>A <see cref="Task"/> that represents the asynchronous invoke.</returns>
        public static Task SendAsync(this IClientProxy clientProxy, string method, object arg1, CancellationToken cancellationToken = default)
        {
            return clientProxy.SendCoreAsync(method, new[] { arg1 }, cancellationToken);
        }

        /// <summary>
        /// Invokes a method on the connection(s) represented by the <see cref="IClientProxy"/> instance.
        /// Does not wait for a response from the receiver.
        /// </summary>
        /// <param name="clientProxy">The <see cref="IClientProxy"/></param>
        /// <param name="method">The name of the method to invoke.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="arg2">The second argument.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.</param>
        /// <returns>A <see cref="Task"/> that represents the asynchronous invoke.</returns>
        public static Task SendAsync(this IClientProxy clientProxy, string method, object arg1, object arg2, CancellationToken cancellationToken = default)
        {
            return clientProxy.SendCoreAsync(method, new[] { arg1, arg2 }, cancellationToken);
        }

        /// <summary>
        /// Invokes a method on the connection(s) represented by the <see cref="IClientProxy"/> instance.
        /// Does not wait for a response from the receiver.
        /// </summary>
        /// <param name="clientProxy">The <see cref="IClientProxy"/></param>
        /// <param name="method">The name of the method to invoke.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="arg2">The second argument.</param>
        /// <param name="arg3">The third argument.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.</param>
        /// <returns>A <see cref="Task"/> that represents the asynchronous invoke.</returns>
        public static Task SendAsync(this IClientProxy clientProxy, string method, object arg1, object arg2, object arg3, CancellationToken cancellationToken = default)
        {
            return clientProxy.SendCoreAsync(method, new[] { arg1, arg2, arg3 }, cancellationToken);
        }

        /// <summary>
        /// Invokes a method on the connection(s) represented by the <see cref="IClientProxy"/> instance.
        /// Does not wait for a response from the receiver.
        /// </summary>
        /// <param name="clientProxy">The <see cref="IClientProxy"/></param>
        /// <param name="method">The name of the method to invoke.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="arg2">The second argument.</param>
        /// <param name="arg3">The third argument.</param>
        /// <param name="arg4">The fourth argument.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.</param>
        /// <returns>A <see cref="Task"/> that represents the asynchronous invoke.</returns>
        public static Task SendAsync(this IClientProxy clientProxy, string method, object arg1, object arg2, object arg3, object arg4, CancellationToken cancellationToken = default)
        {
            return clientProxy.SendCoreAsync(method, new[] { arg1, arg2, arg3, arg4 }, cancellationToken);
        }

        /// <summary>
        /// Invokes a method on the connection(s) represented by the <see cref="IClientProxy"/> instance.
        /// Does not wait for a response from the receiver.
        /// </summary>
        /// <param name="clientProxy">The <see cref="IClientProxy"/></param>
        /// <param name="method">The name of the method to invoke.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="arg2">The second argument.</param>
        /// <param name="arg3">The third argument.</param>
        /// <param name="arg4">The fourth argument.</param>
        /// <param name="arg5">The fifth argument.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.</param>
        /// <returns>A <see cref="Task"/> that represents the asynchronous invoke.</returns>
        public static Task SendAsync(this IClientProxy clientProxy, string method, object arg1, object arg2, object arg3, object arg4, object arg5, CancellationToken cancellationToken = default)
        {
            return clientProxy.SendCoreAsync(method, new[] { arg1, arg2, arg3, arg4, arg5 }, cancellationToken);
        }

        /// <summary>
        /// Invokes a method on the connection(s) represented by the <see cref="IClientProxy"/> instance.
        /// Does not wait for a response from the receiver.
        /// </summary>
        /// <param name="clientProxy">The <see cref="IClientProxy"/></param>
        /// <param name="method">The name of the method to invoke.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="arg2">The second argument.</param>
        /// <param name="arg3">The third argument.</param>
        /// <param name="arg4">The fourth argument.</param>
        /// <param name="arg5">The fifth argument.</param>
        /// <param name="arg6">The sixth argument.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.</param>
        /// <returns>A <see cref="Task"/> that represents the asynchronous invoke.</returns>
        public static Task SendAsync(this IClientProxy clientProxy, string method, object arg1, object arg2, object arg3, object arg4, object arg5, object arg6, CancellationToken cancellationToken = default)
        {
            return clientProxy.SendCoreAsync(method, new[] { arg1, arg2, arg3, arg4, arg5, arg6 }, cancellationToken);
        }

        /// <summary>
        /// Invokes a method on the connection(s) represented by the <see cref="IClientProxy"/> instance.
        /// Does not wait for a response from the receiver.
        /// </summary>
        /// <param name="clientProxy">The <see cref="IClientProxy"/></param>
        /// <param name="method">The name of the method to invoke.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="arg2">The second argument.</param>
        /// <param name="arg3">The third argument.</param>
        /// <param name="arg4">The fourth argument.</param>
        /// <param name="arg5">The fifth argument.</param>
        /// <param name="arg6">The sixth argument.</param>
        /// <param name="arg7">The seventh argument.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.</param>
        /// <returns>A <see cref="Task"/> that represents the asynchronous invoke.</returns>
        public static Task SendAsync(this IClientProxy clientProxy, string method, object arg1, object arg2, object arg3, object arg4, object arg5, object arg6, object arg7, CancellationToken cancellationToken = default)
        {
            return clientProxy.SendCoreAsync(method, new[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7 }, cancellationToken);
        }

        /// <summary>
        /// Invokes a method on the connection(s) represented by the <see cref="IClientProxy"/> instance.
        /// Does not wait for a response from the receiver.
        /// </summary>
        /// <param name="clientProxy">The <see cref="IClientProxy"/></param>
        /// <param name="method">The name of the method to invoke.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="arg2">The second argument.</param>
        /// <param name="arg3">The third argument.</param>
        /// <param name="arg4">The fourth argument.</param>
        /// <param name="arg5">The fifth argument.</param>
        /// <param name="arg6">The sixth argument.</param>
        /// <param name="arg7">The seventh argument.</param>
        /// <param name="arg8">The eigth argument.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.</param>
        /// <returns>A <see cref="Task"/> that represents the asynchronous invoke.</returns>
        public static Task SendAsync(this IClientProxy clientProxy, string method, object arg1, object arg2, object arg3, object arg4, object arg5, object arg6, object arg7, object arg8, CancellationToken cancellationToken = default)
        {
            return clientProxy.SendCoreAsync(method, new[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8 }, cancellationToken);
        }

        /// <summary>
        /// Invokes a method on the connection(s) represented by the <see cref="IClientProxy"/> instance.
        /// Does not wait for a response from the receiver.
        /// </summary>
        /// <param name="clientProxy">The <see cref="IClientProxy"/></param>
        /// <param name="method">The name of the method to invoke.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="arg2">The second argument.</param>
        /// <param name="arg3">The third argument.</param>
        /// <param name="arg4">The fourth argument.</param>
        /// <param name="arg5">The fifth argument.</param>
        /// <param name="arg6">The sixth argument.</param>
        /// <param name="arg7">The seventh argument.</param>
        /// <param name="arg8">The eigth argument.</param>
        /// <param name="arg9">The ninth argument.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.</param>
        /// <returns>A <see cref="Task"/> that represents the asynchronous invoke.</returns>
        public static Task SendAsync(this IClientProxy clientProxy, string method, object arg1, object arg2, object arg3, object arg4, object arg5, object arg6, object arg7, object arg8, object arg9, CancellationToken cancellationToken = default)
        {
            return clientProxy.SendCoreAsync(method, new[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9 }, cancellationToken);
        }

        /// <summary>
        /// Invokes a method on the connection(s) represented by the <see cref="IClientProxy"/> instance.
        /// Does not wait for a response from the receiver.
        /// </summary>
        /// <param name="clientProxy">The <see cref="IClientProxy"/></param>
        /// <param name="method">The name of the method to invoke.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="arg2">The second argument.</param>
        /// <param name="arg3">The third argument.</param>
        /// <param name="arg4">The fourth argument.</param>
        /// <param name="arg5">The fifth argument.</param>
        /// <param name="arg6">The sixth argument.</param>
        /// <param name="arg7">The seventh argument.</param>
        /// <param name="arg8">The eigth argument.</param>
        /// <param name="arg9">The ninth argument.</param>
        /// <param name="arg10">The tenth argument.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.</param>
        /// <returns>A <see cref="Task"/> that represents the asynchronous invoke.</returns>
        public static Task SendAsync(this IClientProxy clientProxy, string method, object arg1, object arg2, object arg3, object arg4, object arg5, object arg6, object arg7, object arg8, object arg9, object arg10, CancellationToken cancellationToken = default)
        {
            return clientProxy.SendCoreAsync(method, new[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10 }, cancellationToken);
        }
    }
}
