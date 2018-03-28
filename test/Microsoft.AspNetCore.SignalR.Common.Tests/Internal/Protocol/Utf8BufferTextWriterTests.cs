// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Xunit;

namespace Microsoft.AspNetCore.SignalR.Common.Tests.Internal.Protocol
{
    public class Utf8BufferTextWriterTests
    {
        public class TestBufferWriter : IBufferWriter<byte>
        {
            private readonly int _bufferSize;
            public List<Memory<byte>> Buffers { get; set; }
            public int Position { get; set; }

            public TestBufferWriter(int bufferSize)
            {
                _bufferSize = bufferSize;

                Buffers = new List<Memory<byte>>();
                Buffers.Add(new Memory<byte>(new byte[_bufferSize]));
            }

            public Memory<byte> CurrentBuffer => Buffers.Last();

            public void Advance(int count)
            {
                Position += count;

                if (Position == _bufferSize)
                {
                    Buffers.Add(new Memory<byte>(new byte[_bufferSize]));
                    Position = 0;
                }
            }

            public Memory<byte> GetMemory(int sizeHint = 0)
            {
                if (sizeHint == 0)
                {
                    // return remaining
                    sizeHint = CurrentBuffer.Length - Position;
                }

                return CurrentBuffer.Slice(Position, sizeHint);
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
            Utf8BufferTextWriter textWriter = new Utf8BufferTextWriter();
            textWriter.SetWriter(bufferWriter);

            textWriter.Write('[');
            Assert.Equal(1, bufferWriter.Position);
            Assert.Equal((byte)'[', bufferWriter.CurrentBuffer.Span[0]);

            textWriter.Write('"');
            Assert.Equal(2, bufferWriter.Position);
            Assert.Equal((byte)'"', bufferWriter.CurrentBuffer.Span[1]);

            textWriter.Write('\u00A3');
            Assert.Equal(2, bufferWriter.Position);

            textWriter.Write('\u00A3');
            Assert.Equal(2, bufferWriter.Position);

            textWriter.Write('"');
            Assert.Equal(7, bufferWriter.Position);
            Assert.Equal((byte)0xC2, bufferWriter.CurrentBuffer.Span[2]);
            Assert.Equal((byte)0xA3, bufferWriter.CurrentBuffer.Span[3]);
            Assert.Equal((byte)0xC2, bufferWriter.CurrentBuffer.Span[4]);
            Assert.Equal((byte)0xA3, bufferWriter.CurrentBuffer.Span[5]);
            Assert.Equal((byte)'"', bufferWriter.CurrentBuffer.Span[6]);

            textWriter.Write(']');
            Assert.Equal(8, bufferWriter.Position);
            Assert.Equal((byte)']', bufferWriter.CurrentBuffer.Span[7]);
        }

        [Fact]
        public void WriteChar_UnicodeLastChar()
        {
            TestBufferWriter bufferWriter = new TestBufferWriter(4096);
            using (Utf8BufferTextWriter textWriter = new Utf8BufferTextWriter())
            {
                textWriter.SetWriter(bufferWriter);

                textWriter.Write('\u00A3');
                Assert.Equal(0, bufferWriter.Position);
            }

            Assert.Equal(2, bufferWriter.Position);

            Assert.Equal((byte)0xC2, bufferWriter.CurrentBuffer.Span[0]);
            Assert.Equal((byte)0xA3, bufferWriter.CurrentBuffer.Span[1]);
        }

        [Fact]
        public void WriteChar_UnicodeAndRunOutOfBufferSpace()
        {
            TestBufferWriter bufferWriter = new TestBufferWriter(4096);
            Utf8BufferTextWriter textWriter = new Utf8BufferTextWriter();
            textWriter.SetWriter(bufferWriter);

            textWriter.Write('[');
            Assert.Equal(1, bufferWriter.Position);
            Assert.Equal((byte)'[', bufferWriter.CurrentBuffer.Span[0]);

            textWriter.Write('"');
            Assert.Equal(2, bufferWriter.Position);
            Assert.Equal((byte)'"', bufferWriter.CurrentBuffer.Span[1]);

            for (int i = 0; i < 2000; i++)
            {
                textWriter.Write('\u00A3');
            }

            textWriter.Write('"');
            Assert.Equal(4003, bufferWriter.Position);
            Assert.Equal((byte)'"', bufferWriter.CurrentBuffer.Span[4002]);

            textWriter.Write(']');
            Assert.Equal(4004, bufferWriter.Position);

            string result = Encoding.UTF8.GetString(bufferWriter.CurrentBuffer.Slice(0, bufferWriter.Position).ToArray());
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
        public void WriteCharArray_SurrogatePairInMultipleCalls()
        {
            string fourCircles = char.ConvertFromUtf32(0x1F01C);

            char[] chars = fourCircles.ToCharArray();

            TestBufferWriter bufferWriter = new TestBufferWriter(4096);
            Utf8BufferTextWriter textWriter = new Utf8BufferTextWriter();
            textWriter.SetWriter(bufferWriter);

            textWriter.Write(chars, 0, 1);

            // Surrogate buffered
            Assert.Equal(0, bufferWriter.Position);

            textWriter.Write(chars, 1, 1);

            Assert.Equal(4, bufferWriter.Position);

            byte[] expectedData = Encoding.UTF8.GetBytes(fourCircles);

            byte[] actualData = bufferWriter.CurrentBuffer.Slice(0, 4).ToArray();

            Assert.Equal(expectedData, actualData);
        }

        [Fact]
        public void WriteCharArray_NonZeroStart()
        {
            TestBufferWriter bufferWriter = new TestBufferWriter(4096);
            Utf8BufferTextWriter textWriter = new Utf8BufferTextWriter();
            textWriter.SetWriter(bufferWriter);

            char[] chars = "Hello world".ToCharArray();

            textWriter.Write(chars, 6, 1);

            Assert.Equal(1, bufferWriter.Position);
            Assert.Equal((byte)'w', bufferWriter.CurrentBuffer.Span[0]);
        }

        [Fact]
        public void WriteCharArray_AcrossMultipleBuffers()
        {
            TestBufferWriter bufferWriter = new TestBufferWriter(2);
            Utf8BufferTextWriter textWriter = new Utf8BufferTextWriter();
            textWriter.SetWriter(bufferWriter);

            char[] chars = "Hello world".ToCharArray();

            textWriter.Write(chars);

            Assert.Equal(6, bufferWriter.Buffers.Count);
            Assert.Equal(1, bufferWriter.Position);

            Assert.Equal((byte)'H', bufferWriter.Buffers[0].Span[0]);
            Assert.Equal((byte)'e', bufferWriter.Buffers[0].Span[1]);
            Assert.Equal((byte)'l', bufferWriter.Buffers[1].Span[0]);
            Assert.Equal((byte)'l', bufferWriter.Buffers[1].Span[1]);
            Assert.Equal((byte)'o', bufferWriter.Buffers[2].Span[0]);
            Assert.Equal((byte)' ', bufferWriter.Buffers[2].Span[1]);
            Assert.Equal((byte)'w', bufferWriter.Buffers[3].Span[0]);
            Assert.Equal((byte)'o', bufferWriter.Buffers[3].Span[1]);
            Assert.Equal((byte)'r', bufferWriter.Buffers[4].Span[0]);
            Assert.Equal((byte)'l', bufferWriter.Buffers[4].Span[1]);
            Assert.Equal((byte)'d', bufferWriter.Buffers[5].Span[0]);
        }

        [Fact]
        public void GetAndReturnCachedBufferTextWriter()
        {
            TestBufferWriter bufferWriter1 = new TestBufferWriter(2);

            var textWriter1 = Utf8BufferTextWriter.Get(bufferWriter1);
            Utf8BufferTextWriter.Return(textWriter1);

            TestBufferWriter bufferWriter2 = new TestBufferWriter(2);

            var textWriter2 = Utf8BufferTextWriter.Get(bufferWriter2);
            Utf8BufferTextWriter.Return(textWriter2);

            Assert.Same(textWriter1, textWriter2);
        }
    }
}
