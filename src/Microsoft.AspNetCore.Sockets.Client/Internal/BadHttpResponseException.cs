// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.IO;
using Microsoft.Extensions.Primitives;

namespace Microsoft.AspNetCore.Sockets.Client.Internal
{
    public sealed class BadHttpResponseException : IOException
    {
        private BadHttpResponseException(string message, int statusCode) : base(message)
        {
            StatusCode = statusCode;
        }

        internal int StatusCode { get; }

        internal static BadHttpResponseException GetException(string data)
        {
            return new BadHttpResponseException(data, 400);
        }
    }
}
