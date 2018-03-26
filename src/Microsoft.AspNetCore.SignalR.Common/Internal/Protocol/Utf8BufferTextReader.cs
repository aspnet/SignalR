// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Microsoft.AspNetCore.SignalR.Internal.Protocol
{
    internal class Utf8BufferTextReader : TextReader
    {
        private ReadOnlyMemory<byte> _utf8Buffer;
        private Decoder _decoder;

        public Utf8BufferTextReader(ReadOnlyMemory<byte> utf8Buffer)
        {
            _utf8Buffer = utf8Buffer;
            _decoder = Encoding.UTF8.GetDecoder();
        }

        public override int Read(char[] buffer, int index, int count)
        {
            if (_utf8Buffer.IsEmpty)
            {
                return 0;
            }

            var source = _utf8Buffer.Span;
            var bytesUsed = 0;
            var charsUsed = 0;
#if NETCOREAPP2_1
            var destination = new Span<char>(buffer, index, count);
            _decoder.Convert(source, destination, true, out bytesUsed, out charsUsed, out var completed);
#else
            unsafe
            {
                fixed (char* destinationChars = &buffer[index])
                fixed (byte* sourceBytes = &MemoryMarshal.GetReference(source))
                {
                    _decoder.Convert(sourceBytes, source.Length, destinationChars, count, true, out bytesUsed, out charsUsed, out var completed);
                }
            }
#endif
            _utf8Buffer = _utf8Buffer.Slice(bytesUsed);
            
            return charsUsed;
        }
    }
}
