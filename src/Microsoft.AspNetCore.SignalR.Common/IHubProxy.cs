// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Channels;

namespace Microsoft.AspNetCore.SignalR
{
    public interface IHubProxy
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="methodName"></param>
        /// <param name="args"></param>
        Task SendAsync(string methodName, params object[] args);
    }

    public interface IHubClientProxy : IHubProxy
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="methodName"></param>
        /// <param name="returnType"></param>
        /// <param name="cancellationToken"></param>
        /// <param name="args"></param>
        /// <returns></returns>
        Task<object> InvokeAsync(string methodName, Type returnType, CancellationToken cancellationToken, params object[] args);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="methodName"></param>
        /// <param name="returnType"></param>
        /// <param name="cancellationToken"></param>
        /// <param name="args"></param>
        /// <returns></returns>
        ReadableChannel<object> Stream(string methodName, Type returnType, CancellationToken cancellationToken, params object[] args);
    }
}
