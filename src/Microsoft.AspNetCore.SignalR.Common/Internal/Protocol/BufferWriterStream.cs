using System;
using System.Buffers;
using System.IO;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.SignalR.Internal.Protocol
{
    public sealed class BufferWriterStream : Stream
    {
        private long _length;
        private readonly IBufferWriter<byte> _bufferWriter;

        public BufferWriterStream(IBufferWriter<byte> bufferWriter)
        {
            _bufferWriter = bufferWriter;
        }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => _length;

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            _bufferWriter.Write(new ReadOnlySpan<byte>(buffer, offset, count));
            _length += count;
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Write(buffer, offset, count);
            return Task.CompletedTask;
        }

#if NETCOREAPP2_1
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> source, CancellationToken cancellationToken = default)
        {
            _bufferWriter.Write(source.Span);
            _length += source.Length;
            return default;
        }
#endif
    }
}