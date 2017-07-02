// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Binary;
using System.IO.Pipelines;

namespace Microsoft.AspNetCore.Sockets.Internal.Formatters
{
    public static class BinaryMessageParser
    {
        public static bool TryParseMessage(ReadableBuffer input, out ReadCursor consumed, out ReadCursor examined, out ReadableBuffer payload)
        {
            consumed = input.Start;
            examined = input.End;
            payload = default(ReadableBuffer);

            if (input.Length < sizeof(long))
            {
                return false;
            }

            Span<byte> lengthSpan;
            if (input.First.Length < sizeof(long))
            {
                // Length split across buffers
                lengthSpan = input.Slice(input.Start, sizeof(long)).ToArray().AsSpan();
            }
            else
            {
                lengthSpan = input.First.Span;
            }

            var length = lengthSpan.ReadBigEndian<long>();

            if (length > Int32.MaxValue)
            {
                throw new FormatException("Messages over 2GB in size are not supported");
            }

            if ((input.Length - sizeof(long)) < length)
            {
                return false;
            }

            payload = input.Slice(sizeof(long), (int)length);

            consumed = payload.End;
            examined = payload.End;
            return true;
        }
    }
}
