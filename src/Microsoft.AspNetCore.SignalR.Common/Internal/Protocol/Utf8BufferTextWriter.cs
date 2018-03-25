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

        private readonly IBufferWriter<byte> _bufferWriter;
        private char[] _charBuffer;
        private int _charBufferPosition;

        public override Encoding Encoding => _utf8NoBom;

        public Utf8BufferTextWriter(IBufferWriter<byte> bufferWriter)
        {
            _bufferWriter = bufferWriter;
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
                    _charBuffer = new char[1024];
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
            var sourceBytesCount = _utf8NoBom.GetByteCount(buffer, index, count);

            var destination = _bufferWriter.GetSpan(sourceBytesCount);

#if NETCOREAPP2_1
            _utf8NoBom.GetBytes(buffer.AsSpan(index, count), destination);
#else
            unsafe
            {
                fixed (char* sourceChars = buffer)
                fixed (byte* destinationBytes = &MemoryMarshal.GetReference(destination))
                {
                    _utf8NoBom.GetBytes(sourceChars, buffer.Length, destinationBytes, sourceBytesCount);
                }
            }
#endif

            _bufferWriter.Advance(sourceBytesCount);
        }

        private void FlushBuffer()
        {
            if (_charBufferPosition > 0)
            {
                WriteInternal(_charBuffer, 0, _charBufferPosition);
                _charBufferPosition = 0;
            }
        }
    }
}