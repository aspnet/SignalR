using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Xunit;

namespace Microsoft.AspNetCore.SignalR.Common.Tests.Internal.Protocol
{
    public class Utf8BufferTextWriterTests
    {
        public class TestBufferWriter : IBufferWriter<byte>
        {
            public Memory<byte> Buffer { get; set; }
            public int Position { get; set; }

            public TestBufferWriter(int bufferSize)
            {
                Buffer = new Memory<byte>(new byte[bufferSize]);
            }

            public void Advance(int count)
            {
                Position += count;
            }

            public Memory<byte> GetMemory(int sizeHint = 0)
            {
                if (sizeHint == 0)
                {
                    // return remaining
                    sizeHint = Buffer.Length - Position;
                }

                return Buffer.Slice(Position, sizeHint);
            }

            public Span<byte> GetSpan(int sizeHint = 0)
            {
                return GetMemory(sizeHint).Span;
            }
        }

        [Fact]
        public void WriteChar_Unicode()
        {
            TestBufferWriter bufferWriter = new TestBufferWriter(4096);
            Utf8BufferTextWriter textWriter = new Utf8BufferTextWriter(bufferWriter);

            textWriter.Write('[');
            Assert.Equal(1, bufferWriter.Position);
            Assert.Equal((byte)'[', bufferWriter.Buffer.Span[0]);

            textWriter.Write('"');
            Assert.Equal(2, bufferWriter.Position);
            Assert.Equal((byte)'"', bufferWriter.Buffer.Span[1]);

            textWriter.Write('\u00A3');
            Assert.Equal(2, bufferWriter.Position);

            textWriter.Write('\u00A3');
            Assert.Equal(2, bufferWriter.Position);

            textWriter.Write('"');
            Assert.Equal(7, bufferWriter.Position);
            Assert.Equal((byte)0xC2, bufferWriter.Buffer.Span[2]);
            Assert.Equal((byte)0xA3, bufferWriter.Buffer.Span[3]);
            Assert.Equal((byte)0xC2, bufferWriter.Buffer.Span[4]);
            Assert.Equal((byte)0xA3, bufferWriter.Buffer.Span[5]);
            Assert.Equal((byte)'"', bufferWriter.Buffer.Span[6]);

            textWriter.Write(']');
            Assert.Equal(8, bufferWriter.Position);
            Assert.Equal((byte)']', bufferWriter.Buffer.Span[7]);
        }

        [Fact]
        public void WriteChar_UnicodeAndRunOutOfBufferSpace()
        {
            TestBufferWriter bufferWriter = new TestBufferWriter(4096);
            Utf8BufferTextWriter textWriter = new Utf8BufferTextWriter(bufferWriter);

            textWriter.Write('[');
            Assert.Equal(1, bufferWriter.Position);
            Assert.Equal((byte)'[', bufferWriter.Buffer.Span[0]);

            textWriter.Write('"');
            Assert.Equal(2, bufferWriter.Position);
            Assert.Equal((byte)'"', bufferWriter.Buffer.Span[1]);

            for (int i = 0; i < 2000; i++)
            {
                textWriter.Write('\u00A3');
            }

            textWriter.Write('"');
            Assert.Equal(4003, bufferWriter.Position);
            Assert.Equal((byte)'"', bufferWriter.Buffer.Span[4002]);

            textWriter.Write(']');
            Assert.Equal(4004, bufferWriter.Position);

            string result = Encoding.UTF8.GetString(bufferWriter.Buffer.Slice(0, bufferWriter.Position).ToArray());
            Assert.Equal(2004, result.Length);

            Assert.Equal('[', result[0]);
            Assert.Equal('"', result[1]);

            for (int i = 0; i < 2000; i++)
            {
                Assert.Equal('\u00A3', result[i + 2]);
            }

            Assert.Equal('"', result[2002]);
            Assert.Equal(']', result[2003]);
        }

        [Fact]
        public void WriteCharArray_NonZeroStart()
        {
            TestBufferWriter bufferWriter = new TestBufferWriter(4096);
            Utf8BufferTextWriter textWriter = new Utf8BufferTextWriter(bufferWriter);

            char[] chars = "Hello world".ToCharArray();

            textWriter.Write(chars, 6, 1);

            Assert.Equal(1, bufferWriter.Position);
            Assert.Equal((byte)'w', bufferWriter.Buffer.Span[0]);
        }
    }
}
