using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Microsoft.AspNetCore.SignalR.Internal.Protocol
{
    /// <summary>
    /// This is a simple text reader implementation over a fixed in memory buffer rented
    /// from the array pool
    /// </summary>
    internal class CharArrayTextReader : TextReader
    {
        private ReadOnlyMemory<byte> _utf8Buffer;

        public CharArrayTextReader(ReadOnlyMemory<byte> utf8Buffer)
        {
            _utf8Buffer = utf8Buffer;
        }

        public override int Read(char[] buffer, int index, int count)
        {
            if (_utf8Buffer.IsEmpty)
            {
                return 0;
            }

            var source = _utf8Buffer.Span;
            var destination = new Span<char>(buffer, index, count);
            var destinationBytesCount = Encoding.UTF8.GetByteCount(buffer, index, count);

            var read = 0;

            // We have then the destination
            if (source.Length > destinationBytesCount)
            {
                var sourceToCopy = source.Slice(0, destinationBytesCount);
#if NETCOREAPP2_1
                read = Encoding.UTF8.GetChars(sourceToCopy, destination);
#else
                unsafe
                {
                    fixed (char* destinationChars = &MemoryMarshal.GetReference(destination))
                    fixed (byte* sourceBytes = &MemoryMarshal.GetReference(sourceToCopy))
                    {
                        read = Encoding.UTF8.GetChars(sourceBytes, source.Length, destinationChars, destination.Length);
                    }
                }
#endif
                _utf8Buffer = _utf8Buffer.Slice(destinationBytesCount);
            }
            else
            {
#if NETCOREAPP2_1
                // WE have less so copy the whole thing
                read = Encoding.UTF8.GetChars(source, destination);
#else
                unsafe
                {
                    fixed (char* destinationChars = &MemoryMarshal.GetReference(destination))
                    fixed (byte* sourceBytes = &MemoryMarshal.GetReference(source))
                    {
                        read = Encoding.UTF8.GetChars(sourceBytes, source.Length, destinationChars, destination.Length);
                    }
                }
#endif
                _utf8Buffer = ReadOnlyMemory<byte>.Empty;
            }

            return read;
        }
    }
}
