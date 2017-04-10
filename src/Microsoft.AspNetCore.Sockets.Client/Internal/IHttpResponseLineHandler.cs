// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Microsoft.AspNetCore.Sockets.Client.Internal
{
    public interface IHttpResponseLineHandler
    {
        void OnStartLine(HttpVersion version, int status, Span<byte> statusText);
    }
}