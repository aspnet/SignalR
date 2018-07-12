// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Text;

namespace System.Buffers
{
    internal abstract class ReadOnlySequenceFactory
    {
        public static ReadOnlySequenceFactory SegmentPerByteFactory { get; } = new BytePerSegmentTestSequenceFactory();

        public abstract ReadOnlySequence<byte> CreateOfSize(int size);
        public abstract ReadOnlySequence<byte> CreateWithContent(byte[] data);

        public ReadOnlySequence<byte> CreateWithContent(string data)
        {
            return CreateWithContent(Encoding.ASCII.GetBytes(data));
        }

        internal class BytePerSegmentTestSequenceFactory : ReadOnlySequenceFactory
        {
            public override ReadOnlySequence<byte> CreateOfSize(int size)
            {
                return CreateWithContent(new byte[size]);
            }

            public override ReadOnlySequence<byte> CreateWithContent(byte[] data)
            {
                var segments = new List<byte[]>();

                segments.Add(Array.Empty<byte>());
                foreach (var b in data)
                {
                    segments.Add(new[] { b });
                    segments.Add(Array.Empty<byte>());
                }

                return CreateSegments(segments.ToArray());
            }
        }

        public static ReadOnlySequence<byte> CreateSegments(params byte[][] inputs)
        {
            if (inputs == null || inputs.Length == 0)
            {
                throw new InvalidOperationException();
            }

            int i = 0;

            BufferSegment last = null;
            BufferSegment first = null;

            do
            {
                byte[] s = inputs[i];
                int length = s.Length;
                int dataOffset = length;
                var chars = new byte[length * 2];

                for (int j = 0; j < length; j++)
                {
                    chars[dataOffset + j] = s[j];
                }

                // Create a segment that has offset relative to the OwnedMemory and OwnedMemory itself has offset relative to array
                var memory = new Memory<byte>(chars).Slice(length, length);

                if (first == null)
                {
                    first = new BufferSegment(memory);
                    last = first;
                }
                else
                {
                    last = last.Append(memory);
                }
                i++;
            } while (i < inputs.Length);

            return new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
        }
    }
}
