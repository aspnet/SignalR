// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.Buffers.Binary;

namespace Microsoft.AspNetCore.SignalR.Internal.Formatters
{
    public static class BinaryMessageParser
    {
        private const int MaxLengthPrefixSize = 5;

        public static bool TryParseMessage(ref ReadOnlySequence<byte> buffer, out ReadOnlySequence<byte> payload)
        {
            const int numBytes = 4;
            if (buffer.Length < numBytes)
            {
                // No length prefix, don't bother
                payload = default;
                return false;
            }

            if (!BinaryPrimitives.TryReadUInt32BigEndian(buffer.First.Span, out var length))
            {
                // Should be super rare
                length = BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(0, numBytes).ToArray());
            }

            if (length > Int32.MaxValue)
            {
                throw new FormatException("Messages over 2GB in size are not supported.");
            }

            // We don't have enough data
            if (length > buffer.Length - numBytes)
            {
                payload = default;
                return false;
            }

            // Get the payload
            payload = buffer.Slice(numBytes, (int)length);

            // Skip the payload
            buffer = buffer.Slice(numBytes + (int)length);
            return true;
        }
    }
}