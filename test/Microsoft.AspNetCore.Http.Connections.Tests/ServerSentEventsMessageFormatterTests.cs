// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.AspNetCore.Http.Connections.Internal;
using Xunit;

namespace Microsoft.AspNetCore.Http.Connections.Tests
{
    public class ServerSentEventsMessageFormatterTests
    {
        [Theory]
        [MemberData(nameof(PayloadData))]
        public void WriteTextMessageFromSingleSegment(string encoded, string payload)
        {
            var output = new MemoryStream();
            ServerSentEventsMessageFormatter.WriteMessage(new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes(payload)), output);

            Assert.Equal(encoded, Encoding.UTF8.GetString(output.ToArray()));
        }

        [Theory]
        [MemberData(nameof(PayloadData))]
        public void WriteTextMessageFromMultipleSegments(string encoded, string payload)
        {
            var segment = ReadOnlySequenceFactory.SegmentPerByteFactory.CreateWithContent(Encoding.UTF8.GetBytes(payload));

            var output = new MemoryStream();
            ServerSentEventsMessageFormatter.WriteMessage(segment, output);

            Assert.Equal(encoded, Encoding.UTF8.GetString(output.ToArray()));
        }

        public static IEnumerable<object[]> PayloadData => new List<object[]>
        {
            new object[] { "\r\n", "" },
            new object[] { "data: Hello, World\r\n\r\n", "Hello, World" },
            new object[] { "data: Hello\r\ndata: World\r\n\r\n", "Hello\r\nWorld" },
            new object[] { "data: Hello\r\ndata: World\r\n\r\n", "Hello\nWorld" },
            new object[] { "data: Hello\r\ndata: \r\n\r\n", "Hello\n" },
            new object[] { "data: Hello\r\ndata: \r\n\r\n", "Hello\r\n" },
        };
    }
}
