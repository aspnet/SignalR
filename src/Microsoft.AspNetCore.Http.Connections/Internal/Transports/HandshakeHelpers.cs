// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace Microsoft.AspNetCore.WebSockets.Internal
{
    internal static class HandshakeHelpers
    {
        public static void GenerateResponseHeaders(string key, string subProtocol, HttpContext context)
        {
            context.Response.Headers[Constants.Headers.Connection] = Constants.Headers.ConnectionUpgrade;
            context.Response.Headers[Constants.Headers.Upgrade] = Constants.Headers.UpgradeWebSocket;
            context.Response.Headers[Constants.Headers.SecWebSocketAccept] = CreateResponseKey(key);

            if (!string.IsNullOrWhiteSpace(subProtocol))
            {
                context.Response.Headers[Constants.Headers.SecWebSocketProtocol] = subProtocol;
            }
        }

        private static string CreateResponseKey(string requestKey)
        {
            // "The value of this header field is constructed by concatenating /key/, defined above in step 4
            // in Section 4.2.2, with the string "258EAFA5- E914-47DA-95CA-C5AB0DC85B11", taking the SHA-1 hash of
            // this concatenated value to obtain a 20-byte value and base64-encoding"
            // https://tools.ietf.org/html/rfc6455#section-4.2.2

            using (var algorithm = SHA1.Create())
            {
                string merged = requestKey + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
                byte[] mergedBytes = Encoding.UTF8.GetBytes(merged);
                byte[] hashedBytes = algorithm.ComputeHash(mergedBytes);
                return Convert.ToBase64String(hashedBytes);
            }
        }
    }
}