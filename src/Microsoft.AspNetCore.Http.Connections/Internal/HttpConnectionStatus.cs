// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Microsoft.AspNetCore.Http.Connections.Internal
{
    public enum HttpConnectionStatus
    {
        /// <summary>
        /// Indicates the connection is idle, and can be disposed if the timeout period has elapsed.
        /// </summary>
        Inactive,

        /// <summary>
        /// Indicates that the connection is active, and should not be disposed.
        /// </summary>
        Active,

        /// <summary>
        /// Indicates that the connection has been disposed.
        /// </summary>
        Disposed,
    }
}
