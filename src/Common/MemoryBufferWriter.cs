// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.Internal
{
    internal sealed class MemoryBufferWriter : Stream, IBufferWriter<byte>
    {
        [ThreadStatic]
        private static MemoryBufferWriter _cachedInstance;

#if DEBUG
        private bool _inUse;
#endif

        private readonly int _minimumSegmentSize;
        private int _bytesWritten;

        private List<byte[]> _fullSegments;
        private byte[] _currentSegment;
        private int _position;

        public MemoryBufferWriter(int minimumSegmentSize = 4096)
        {
            _minimumSegmentSize = minimumSegmentSize;
        }

        public override long Length => _bytesWritten;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public static MemoryBufferWriter Get()
        {
            var writer = _cachedInstance;
            if (writer == null)
            {
                writer = new MemoryBufferWriter();
            }
            else
            {
                // Taken off the thread static
                _cachedInstance = null;
            }
#if DEBUG
            if (writer._inUse)
            {
                throw new InvalidOperationException("The reader wasn't returned!");
            }

            writer._inUse = true;
#endif

            return writer;
        }

        public static void Return(MemoryBufferWriter writer)
        {
            // Skip write barrier if already have one cached
            if (_cachedInstance == null)
            {
                _cachedInstance = writer;
            }
#if DEBUG
            writer._inUse = false;
#endif
            writer.Reset();
        }

        public void Reset()
        {
            // Don't resolve the ArrayPool per loop
            // Also Jit doesn't devirtualize it, yet https://github.com/dotnet/coreclr/pull/15743
            var arraypool = ArrayPool<byte>.Shared;
            var fullSegments = _fullSegments;
            if (fullSegments != null)
            {
                var count = fullSegments.Count;
                for (var i = 0; i < count; i++)
                {
                    arraypool.Return(fullSegments[i]);
                }

                fullSegments.Clear();
            }

            var currentSegment = _currentSegment;
            if (currentSegment != null)
            {
                arraypool.Return(currentSegment);
                _currentSegment = null;
            }

            _bytesWritten = 0;
            _position = 0;
        }

        public void Advance(int count)
        {
            _bytesWritten += count;
            _position += count;
        }

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            var currentSegment = EnsureCapacity(sizeHint);

            var position = _position;
            return currentSegment.AsMemory(position, currentSegment.Length - position);
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            var currentSegment = EnsureCapacity(sizeHint);

            var position = _position;
            return currentSegment.AsSpan(position, currentSegment.Length - position);
        }

        public void CopyTo(IBufferWriter<byte> destination)
        {
            var fullSegments = _fullSegments;
            if (fullSegments != null)
            {
                // Copy full segments
                var count = fullSegments.Count;
                for (var i = 0; i < count; i++)
                {
                    destination.Write(fullSegments[i]);
                }
            }

            destination.Write(_currentSegment.AsSpan(0, _position));
        }

        public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        {
            if (_fullSegments == null)
            {
                // There is only one segment so write without async
                return destination.WriteAsync(_currentSegment, 0, _position);
            }

            return CopyToSlowAsync(destination);
        }

        private byte[] EnsureCapacity(int sizeHint)
        {
            // TODO: Use sizeHint
            var currentSegment = _currentSegment;
            if (currentSegment != null && _position < currentSegment.Length)
            {
                // We have capacity in the current segment
                return currentSegment;
            }

            return AddSegment();
        }

        private byte[] AddSegment()
        {
            var currentSegment = _currentSegment;
            if (currentSegment != null)
            {
                // We're adding a segment to the list
                var fullSegments = _fullSegments;
                if (fullSegments == null)
                {
                    fullSegments =  new List<byte[]>();
                    _fullSegments = fullSegments;
                }

                fullSegments.Add(currentSegment);
            }

            _position = 0;
            currentSegment = ArrayPool<byte>.Shared.Rent(_minimumSegmentSize);
            _currentSegment = currentSegment;

            return currentSegment;
        }

        private async Task CopyToSlowAsync(Stream destination)
        {
            var fullSegments = _fullSegments;
            if (fullSegments != null)
            {
                // Copy full segments                
                var count = fullSegments.Count;
                for (var i = 0; i < count; i++)
                {
                    var segment = fullSegments[i];
                    await destination.WriteAsync(segment, 0, segment.Length);
                }
            }

            await destination.WriteAsync(_currentSegment, 0, _position);
        }

        public byte[] ToArray()
        {
            var currentSegment = _currentSegment;
            if (currentSegment == null)
            {
                return Array.Empty<byte>();
            }

            var result = new byte[_bytesWritten];

            var totalWritten = 0;

            var fullSegments = _fullSegments;
            if (fullSegments != null)
            {
                // Copy full segments
                var count = fullSegments.Count;
                for (var i = 0; i < count; i++)
                {
                    var segment = fullSegments[i];
                    segment.CopyTo(result, totalWritten);
                    totalWritten += segment.Length;
                }
            }

            // Copy current incomplete segment
            currentSegment.AsSpan(0, _position).CopyTo(result.AsSpan(totalWritten));

            return result;
        }

        public override void Flush() { }
        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        public override void WriteByte(byte value)
        {
            var position = _position;
            var currentSegment = _currentSegment;
            if (currentSegment != null && (uint)position < (uint)currentSegment.Length)
            {
                currentSegment[position] = value;
                _position = position + 1;
            }
            else
            {
                currentSegment = AddSegment();
                currentSegment[0] = value;
                _position = 1;
            }

            _bytesWritten++;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            var position = _position;
            var currentSegment = _currentSegment;
            if (currentSegment == null)
            {
                currentSegment = AddSegment();
            }

            if (position < currentSegment.Length - count)
            {
                Buffer.BlockCopy(buffer, offset, currentSegment, position, count);

                _position = position + count;
                _bytesWritten += count;
            }
            else
            {
                WriteMultiSegment(buffer.AsSpan(offset, count));
            }
        }

#if NETCOREAPP2_1
        public override void Write(ReadOnlySpan<byte> span)
        {
            var currentSegment = _currentSegment;
            if (currentSegment == null)
            {
                currentSegment = AddSegment();
            }

            var position = _position;
            if (span.TryCopyTo(currentSegment.AsSpan().Slice(position)))
            {
                _position = position + span.Length;
                _bytesWritten += span.Length;
            }
            else
            {
                WriteMultiSegment(span);
            }
        }
#endif

        private void WriteMultiSegment(in ReadOnlySpan<byte> source)
        {
            var input = source;
            var currentSegment = _currentSegment;

            var position = _position;
            while (true)
            {
                int writeSize = Math.Min(currentSegment.Length - position, input.Length);
                if (writeSize > 0)
                {
                    input.Slice(0, writeSize).CopyTo(currentSegment.AsSpan(position));
                    _bytesWritten += writeSize;
                }
                if (input.Length > writeSize)
                {
                    input = input.Slice(writeSize);
                    currentSegment = AddSegment();
                    position = 0;
                    continue;
                }

                _position = position + writeSize;
                return;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Reset();
            }
        }
    }
}