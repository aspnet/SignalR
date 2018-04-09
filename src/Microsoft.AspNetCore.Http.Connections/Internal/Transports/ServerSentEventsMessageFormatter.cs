// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.Http.Connections.Internal
{
    public static class ServerSentEventsMessageFormatter
    {
        private static readonly byte[] DataPrefix = new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a', (byte)':', (byte)' ' };
        private static readonly byte[] Newline = new[] { (byte)'\r', (byte)'\n' };

        private const byte LineFeed = (byte)'\n';

        public static async Task WriteMessageAsync(ReadOnlySequence<byte> payload, Stream output)
        {
            var ms = new MemoryStream();

            // TODO: There are 2 improvements to be made here
            // 1. Don't convert the entire payload into an array if if's multi-segmented.
            // 2. Don't allocate the memory stream unless the payload contains \n. If it doesn't we can just write the buffers directly
            // to the stream without modification. While it does mean that there will be smaller writes, should be fine for the most part
            // since we're using reasonably sized buffers.

            WriteMessage(payload, ms);

            ms.Position = 0;

            await ms.CopyToAsync(output);
        }

        public static void WriteMessage(ReadOnlySequence<byte> sequence, Stream output)
        {
            // We can't just use while(payload.Length > 0) because we need to write a blank final "data: " line
            // if the payload ends in a newline. For example, consider the following payload:
            //   "Hello\n"
            // It needs to be written as:
            //   data: Hello\r\n
            //   data: \r\n
            //   \r\n
            // Since we slice past the newline when we find it, after writing "Hello" in the previous example, we'll
            // end up with an empty payload buffer, BUT we need to write it as an empty 'data:' line, so we need
            // to use a condition that ensure the only time we stop writing is when we write the slice after the final
            // newline.

            if (sequence.Length > 0)
            {
                var position = sequence.Start;
                var segmentFinished = true;
                var previousSequenceEndedWithCarrageReturn = false;

                while (true)
                {
                    var trailingNewLine = false;

                    output.Write(DataPrefix, 0, DataPrefix.Length);

                    // Write the payload
                    while (!segmentFinished || sequence.TryGet(ref position, out var memory))
                    {
                        // Ignore any empty memory segments
                        if (memory.Length == 0)
                        {
                            segmentFinished = true;
                            continue;
                        }

                        // Successfully got memory
                        if (segmentFinished)
                        {
                            segmentFinished = false;
                        }

                        var span = memory.Span;

                        // Handle potential situation \r and \n are split over two segments
                        if (previousSequenceEndedWithCarrageReturn)
                        {
                            if (span[0] == '\n')
                            {
                                // Carrage return has already been written
                                output.WriteByte((byte) '\n');

                                // Break to begin new line
                                memory = memory.Slice(1);
                                break;
                            }

                            previousSequenceEndedWithCarrageReturn = false;
                        }

                        // Seek to the end of buffer or newline
                        var sliceEnd = span.IndexOf(LineFeed);

                        // Line feed not found
                        if (sliceEnd == -1)
                        {
                            if (span[span.Length - 1] == '\r')
                            {
                                // Handle potential situation \r and \n are split over two segments
                                previousSequenceEndedWithCarrageReturn = true;
                            }
                            WriteSpan(output, memory);

                            segmentFinished = true;

                            // Continue to add more content to current line
                            continue;
                        }

                        var resolvedSliceEnd = sliceEnd;
                        if (resolvedSliceEnd > 0 && span[resolvedSliceEnd - 1] == '\r')
                        {
                            // Update slice to end before carrage return
                            resolvedSliceEnd--;
                        }

                        if (resolvedSliceEnd == 0)
                        {
                            // Segment only contained new line
                            segmentFinished = true;
                            output.Write(Newline, 0, Newline.Length);

                            break;
                        }

                        WriteSpan(output, memory.Slice(0, resolvedSliceEnd));
                        memory = memory.Slice(sliceEnd + 1);
                        trailingNewLine = true;
                        segmentFinished = memory.Length == 0;
                        if (!segmentFinished)
                        {
                            output.Write(Newline, 0, Newline.Length);
                        }

                        // Break to begin new line
                        break;
                    }

                    // Test if last segment
                    if (segmentFinished && position.GetObject() == null)
                    {
                        // Content finished with \n or \r\n so add final line with no data
                        if (trailingNewLine)
                        {
                            output.Write(Newline, 0, Newline.Length);
                            output.Write(DataPrefix, 0, DataPrefix.Length);
                        }

                        output.Write(Newline, 0, Newline.Length);
                        break;
                    }
                }
            }

            // Final new line
            output.Write(Newline, 0, Newline.Length);
        }

        private static void WriteSpan(Stream output, ReadOnlyMemory<byte> data)
        {
            if (data.Length > 0)
            {
#if NETCOREAPP2_1
                output.Write(data.Span);
#else
                var isArray = MemoryMarshal.TryGetArray(data, out var segment);
                Debug.Assert(isArray);
                output.Write(segment.Array, segment.Offset, segment.Count);
#endif
            }
        }
    }
}
