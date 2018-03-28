// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Microsoft.AspNetCore.SignalR.Internal.Protocol
{
    internal sealed class Utf8BufferTextWriter : TextWriter
    {
        private static readonly UTF8Encoding _utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        [ThreadStatic]
        private static Utf8BufferTextWriter _cachedInstance;

        private readonly Encoder _encoder;
        private IBufferWriter<byte> _bufferWriter;
        private char[] _charBuffer;
        private int _charBufferPosition;

#if DEBUG
        private bool _inUse;
#endif

        public override Encoding Encoding => _utf8NoBom;

        public Utf8BufferTextWriter()
        {
            _encoder = _utf8NoBom.GetEncoder();
        }

        public static Utf8BufferTextWriter Get(IBufferWriter<byte> bufferWriter)
        {
            var writer = _cachedInstance;
            if (writer == null)
            {
                writer = new Utf8BufferTextWriter();
            }

            // Taken off the the thread static
            _cachedInstance = null;
#if DEBUG
            if (writer._inUse)
            {
                throw new InvalidOperationException("The writer wasn't returned!");
            }

            writer._inUse = true;
#endif
            writer.SetWriter(bufferWriter);
            return writer;
        }

        public static void Return(Utf8BufferTextWriter writer)
        {
            _cachedInstance = writer;
#if DEBUG
            writer._inUse = false;
#endif
        }

        public void SetWriter(IBufferWriter<byte> bufferWriter)
        {
            _bufferWriter = bufferWriter;
            _encoder.Reset();
        }

        public override void Write(char[] buffer, int index, int count)
        {
            FlushBuffer();

            WriteInternal(buffer, index, count);
        }

        public override void Write(char[] buffer)
        {
            FlushBuffer();

            WriteInternal(buffer, 0, buffer.Length);
        }

        public override void Write(char value)
        {
            if (value <= 127)
            {
                FlushBuffer();

                var destination = _bufferWriter.GetSpan(1);

                destination[0] = (byte)value;
                _bufferWriter.Advance(1);
            }
            else
            {
                // Json.NET only writes ASCII characters by themselves, e.g. {}[], etc
                // this should be an exceptional case
                if (_charBuffer == null)
                {
                    _charBuffer = new char[32];
                }

                // Run out of buffer space
                if (_charBufferPosition == _charBuffer.Length)
                {
                    FlushBuffer();
                }

                _charBuffer[_charBufferPosition++] = value;
            }
        }

        private void WriteInternal(char[] buffer, int index, int count)
        {
            var currentIndex = index;
            var charsRemaining = count;
            while (charsRemaining > 0)
            {
                // The destination byte array might not be large enough so multiple writes are sometimes required
                var destination = _bufferWriter.GetSpan();

                var bytesUsed = 0;
                var charsUsed = 0;
#if NETCOREAPP2_1
                _encoder.Convert(buffer.AsSpan(currentIndex, charsRemaining), destination, false, out charsUsed, out bytesUsed, out _);
#else
                unsafe
                {
                    fixed (char* sourceChars = &buffer[currentIndex])
                    fixed (byte* destinationBytes = &MemoryMarshal.GetReference(destination))
                    {
                        _encoder.Convert(sourceChars, charsRemaining, destinationBytes, destination.Length, false, out charsUsed, out bytesUsed, out _);
                    }
                }
#endif

                charsRemaining -= charsUsed;
                currentIndex += charsUsed;
                _bufferWriter.Advance(bytesUsed);
            }
        }

        private void FlushBuffer()
        {
            if (_charBufferPosition > 0)
            {
                WriteInternal(_charBuffer, 0, _charBufferPosition);
                _charBufferPosition = 0;
            }
        }

        public override void Flush()
        {
            FlushBuffer();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (disposing)
            {
                FlushBuffer();
            }
        }
    }
}