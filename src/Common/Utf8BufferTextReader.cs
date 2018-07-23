// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Microsoft.AspNetCore.SignalR.Internal
{
    internal sealed class Utf8BufferTextReader : TextReader
    {
        private readonly Decoder _decoder;
        private ReadOnlySequence<byte> _utf8Buffer;
        private SequencePosition _position;
        private byte[] _buffer;
        private int _offset;
        private int _end;

        [ThreadStatic]
        private static Utf8BufferTextReader _cachedInstance;

#if DEBUG
        private bool _inUse;
#endif

        public Utf8BufferTextReader()
        {
            _decoder = Encoding.UTF8.GetDecoder();
        }

        public static Utf8BufferTextReader Get(in ReadOnlySequence<byte> utf8Buffer)
        {
            var reader = _cachedInstance;
            if (reader == null)
            {
                reader = new Utf8BufferTextReader();
            }

            // Taken off the the thread static
            _cachedInstance = null;
#if DEBUG
            if (reader._inUse)
            {
                throw new InvalidOperationException("The reader wasn't returned!");
            }

            reader._inUse = true;
#endif
            reader.SetBuffer(utf8Buffer);
            return reader;
        }

        public static void Return(Utf8BufferTextReader reader)
        {
            _cachedInstance = reader;
#if DEBUG
            reader._inUse = false;
#endif
            reader._end = -1;
            reader._offset = -1;
            reader._buffer = null;
        }

        public void SetBuffer(in ReadOnlySequence<byte> utf8Buffer)
        {
            _utf8Buffer = utf8Buffer;
            _position = utf8Buffer.Start;

            GetNextSegment();

            _decoder.Reset();
        }

        public override int Read(char[] buffer, int index, int count)
        {
            var source = _buffer;
            var offset = _offset;
            var end = _end;

            if ((uint)offset >= (uint)end)
            {
                return 0;
            }

            var bytesUsed = 0;
            var charsUsed = 0;

            unsafe
            {
                fixed (char* destinationChars = &buffer[index])
                fixed (byte* sourceBytes = &source[offset])
                {
                    _decoder.Convert(sourceBytes, end - offset, destinationChars, count, false, out bytesUsed, out charsUsed, out var completed);
                }
            }
            
            offset += bytesUsed;

            if ((uint)offset >= (uint)end)
            {
                GetNextSegment();
            }
            else
            {
                _offset = offset;
            }

            return charsUsed;
        }

        private void GetNextSegment()
        {
            if (_utf8Buffer.TryGet(ref _position, out var memory))
            {
                MemoryMarshal.TryGetArray(memory, out var segment);
                _buffer = segment.Array;
                _offset = segment.Offset;
                _end = _offset + segment.Count;
            }
        }
    }
}
