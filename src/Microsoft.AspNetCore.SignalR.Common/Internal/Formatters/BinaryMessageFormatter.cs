// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.Buffers.Binary;

namespace Microsoft.AspNetCore.SignalR.Internal.Formatters
{
    public static class BinaryMessageFormatter
    {
        public static void WriteLengthPrefix(long length, IBufferWriter<byte> output)
        {
            // TODO: Just pass an int
            BinaryPrimitives.WriteInt32BigEndian(output.GetSpan(4), (int)length);
            output.Advance(4);
        }
    }
}
