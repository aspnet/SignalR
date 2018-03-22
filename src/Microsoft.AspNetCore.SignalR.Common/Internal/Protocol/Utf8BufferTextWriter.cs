using System.Buffers;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Microsoft.AspNetCore.SignalR.Internal.Protocol
{
    internal sealed class Utf8BufferTextWriter : TextWriter
    {
        private readonly IBufferWriter<byte> _bufferWriter;

        public override Encoding Encoding => Encoding.UTF8;

        public Utf8BufferTextWriter(IBufferWriter<byte> bufferWriter)
        {
            _bufferWriter = bufferWriter;
        }

        public override void Write(char[] buffer, int index, int count)
        {
            var sourceBytesCount = Encoding.UTF8.GetByteCount(buffer, index, count);

            // TODO: Consider getting span once and writing content until it is full
            // Avoids overhead of getting span and advancing with every write
            var destination = _bufferWriter.GetSpan(sourceBytesCount);

#if NETCOREAPP2_1
            Encoding.UTF8.GetBytes(buffer, destination);
#else
            unsafe
            {
                fixed (char* sourceChars = buffer)
                fixed (byte* destinationBytes = &MemoryMarshal.GetReference(destination))
                {
                    Encoding.UTF8.GetBytes(sourceChars, buffer.Length, destinationBytes, sourceBytesCount);
                }
            }
#endif

            _bufferWriter.Advance(sourceBytesCount);
        }

        public override void Write(char value)
        {
            var destination = _bufferWriter.GetSpan(1);

            // TODO: Handle multi-byte chars
            destination[0] = (byte) value;
            _bufferWriter.Advance(1);
        }

        public override void Write(char[] buffer)
        {
            Write(buffer, 0, buffer.Length);
        }
    }
}