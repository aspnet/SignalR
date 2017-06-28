﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Xunit;

namespace Microsoft.AspNetCore.SignalR.Common.Tests.Internal.Protocol
{
    public class MessagePackHubProtocolTests
    {
        private static readonly MessagePackHubProtocol _hubProtocol = new MessagePackHubProtocol();

        public static IEnumerable<object[]> TestMessages => new[]
        {
            new object[] { new[] { new InvocationMessage("xyz", /*nonBlocking*/ false, "method") } },
            new object[] { new[] { new InvocationMessage("xyz", /*nonBlocking*/ true, "method") } },
            new object[] { new[] { new InvocationMessage("xyz", /*nonBlocking*/ true, "method", new object[] { null }) } },
            new object[] { new[] { new InvocationMessage("xyz", /*nonBlocking*/ true, "method", 42) } },
            new object[] { new[] { new InvocationMessage("xyz", /*nonBlocking*/ true, "method", 42, "string") } },
            new object[] { new[] { new InvocationMessage("xyz", /*nonBlocking*/ true, "method", 42, "string", new CustomObject()) } },
            new object[] { new[] { new InvocationMessage("xyz", /*nonBlocking*/ true, "method", new[] { new CustomObject(), new CustomObject() }) } },

            new object[] { new[] { new CompletionMessage("xyz", error: "Error not found!", result: null, hasResult: false) } },
            new object[] { new[] { new CompletionMessage("xyz", error: null, result: null, hasResult: false) } },
            new object[] { new[] { new CompletionMessage("xyz", error: null, result: null, hasResult: true) } },
            new object[] { new[] { new CompletionMessage("xyz", error: null, result: 42, hasResult: true) } },
            new object[] { new[] { new CompletionMessage("xyz", error: null, result: 42.0f, hasResult: true) } },
            new object[] { new[] { new CompletionMessage("xyz", error: null, result: "string", hasResult: true) } },
            new object[] { new[] { new CompletionMessage("xyz", error: null, result: true, hasResult: true) } },
            new object[] { new[] { new CompletionMessage("xyz", error: null, result: new CustomObject(), hasResult: true) } },
            new object[] { new[] { new CompletionMessage("xyz", error: null, result: new[] { new CustomObject(), new CustomObject() }, hasResult: true) } },

            new object[] { new[] { new StreamItemMessage("xyz", null) } },
            new object[] { new[] { new StreamItemMessage("xyz", 42) } },
            new object[] { new[] { new StreamItemMessage("xyz", 42.0f) } },
            new object[] { new[] { new StreamItemMessage("xyz", "string") } },
            new object[] { new[] { new StreamItemMessage("xyz", true) } },
            new object[] { new[] { new StreamItemMessage("xyz", new CustomObject()) } },
            new object[] { new[] { new StreamItemMessage("xyz", new[] { new CustomObject(), new CustomObject() }) } },

            new object[]
            {
                new HubMessage[]
                {
                    new InvocationMessage("xyz", /*nonBlocking*/ true, "method", 42, "string", new CustomObject()),
                    new CompletionMessage("xyz", error: null, result: 42, hasResult: true),
                    new StreamItemMessage("xyz", null),
                    new CompletionMessage("xyz", error: null, result: new CustomObject(), hasResult: true)
                }
            }
        };

        [Theory]
        [MemberData(nameof(TestMessages))]
        public void CanRoundTripInvocationMessage(HubMessage[] hubMessages)
        {
            using (var memoryStream = new MemoryStream())
            {
                foreach (var hubMessage in hubMessages)
                {
                    _hubProtocol.TryWriteMessage(hubMessage, memoryStream);
                }

                _hubProtocol.TryParseMessages(
                    new ReadOnlySpan<byte>(memoryStream.ToArray()), new CompositeTestBinder(hubMessages), out var messages);

                Assert.Equal(hubMessages, messages, TestHubMessageEqualityComparer.Instance);
            }
        }

        public static IEnumerable<object[]> InvalidPayloads => new[]
        {
            new object[] { new byte[0], "Message type is missing." },
            new object[] { new byte[] { 0x0a } , "Invalid message type: 10." },

            // InvocationMessage
            new object[] { new byte[] { 0x01 }, "Reading 'invocationId' as String failed." }, // invocationId missing
            new object[] { new byte[] { 0x01, 0xc2 }, "Reading 'invocationId' as String failed." }, // 0xc2 is Bool false
            new object[] { new byte[] { 0x01, 0xa3, 0x78, 0x79, 0x7a }, "Reading 'nonBlocking' as Boolean failed." }, // nonBlocking missing
            new object[] { new byte[] { 0x01, 0xa3, 0x78, 0x79, 0x7a, 0x00 }, "Reading 'nonBlocking' as Boolean failed." }, // nonBlocking is not bool
            new object[] { new byte[] { 0x01, 0xa3, 0x78, 0x79, 0x7a, 0xc2 }, "Reading 'target' as String failed." }, // target missing
            new object[] { new byte[] { 0x01, 0xa3, 0x78, 0x79, 0x7a, 0xc2, 0x00 }, "Reading 'target' as String failed." }, // 0x00 is Int
            new object[] { new byte[] { 0x01, 0xa3, 0x78, 0x79, 0x7a, 0xc2, 0xa1 }, "Reading 'target' as String failed." }, // string is cut
            new object[] { new byte[] { 0x01, 0xa3, 0x78, 0x79, 0x7a, 0xc2, 0xa1, 0x78 }, "Reading array length for 'arguments' failed." }, // array is missing
            new object[] { new byte[] { 0x01, 0xa3, 0x78, 0x79, 0x7a, 0xc2, 0xa1, 0x78, 0x00 }, "Reading array length for 'arguments' failed." }, // 0x00 is not array marker
            new object[] { new byte[] { 0x01, 0xa3, 0x78, 0x79, 0x7a, 0xc2, 0xa1, 0x78, 0x91 }, "Deserializing object of the `String` type for 'argument' failed." }, // array is missing elements
            new object[] { new byte[] { 0x01, 0xa3, 0x78, 0x79, 0x7a, 0xc2, 0xa1, 0x78, 0x91, 0xa2, 0x78 }, "Deserializing object of the `String` type for 'argument' failed." }, // array element is cut
            new object[] { new byte[] { 0x01, 0xa3, 0x78, 0x79, 0x7a, 0xc2, 0xa1, 0x78, 0x92, 0xa0, 0x00 }, "Target method expects 1 arguments(s) but invocation has 2 argument(s)." }, // argument count does not match binder argument count
            new object[] { new byte[] { 0x01, 0xa3, 0x78, 0x79, 0x7a, 0xc2, 0xa1, 0x78, 0x91, 0x00 }, "Deserializing object of the `String` type for 'argument' failed." }, // argument type mismatch

            // StreamItemMessage
            new object[] { new byte[] { 0x02 }, "Reading 'invocationId' as String failed." }, // 0xc2 is Bool false
            new object[] { new byte[] { 0x02, 0xc2 }, "Reading 'invocationId' as String failed." }, // 0xc2 is Bool false
            new object[] { new byte[] { 0x02, 0xa3, 0x78, 0x79, 0x7a }, "Deserializing object of the `String` type for 'item' failed." }, // item is missing
            new object[] { new byte[] { 0x02, 0xa3, 0x78, 0x79, 0x7a, 0x00 }, "Deserializing object of the `String` type for 'item' failed." }, // item type mismatch

            // CompletionMessage
            new object[] { new byte[] { 0x03 }, "Reading 'invocationId' as String failed." }, // 0xc2 is Bool false
            new object[] { new byte[] { 0x03, 0xc2 }, "Reading 'invocationId' as String failed." }, // 0xc2 is Bool false
            new object[] { new byte[] { 0x03, 0xa3, 0x78, 0x79, 0x7a, 0xc2 }, "Reading 'error' as String failed." }, // 0xc2 is Bool false
            new object[] { new byte[] { 0x03, 0xa3, 0x78, 0x79, 0x7a, 0xa1 }, "Reading 'error' as String failed." }, // error is cut
            new object[] { new byte[] { 0x03, 0xa3, 0x78, 0x79, 0x7a, 0xc0 }, "Reading 'hasResult' as Boolean failed." }, // hasResult missing
            new object[] { new byte[] { 0x03, 0xa3, 0x78, 0x79, 0x7a, 0xc0, 0xa0 }, "Reading 'hasResult' as Boolean failed." }, // 0xa0 is string
            new object[] { new byte[] { 0x03, 0xa3, 0x78, 0x79, 0x7a, 0xc0, 0xc3 }, "Deserializing object of the `String` type for 'argument' failed." }, // result missing
            new object[] { new byte[] { 0x03, 0xa3, 0x78, 0x79, 0x7a, 0xc0, 0xc3, 0xa9 }, "Deserializing object of the `String` type for 'argument' failed." }, // result is cut
            new object[] { new byte[] { 0x03, 0xa3, 0x78, 0x79, 0x7a, 0xc0, 0xc3, 0x00 }, "Deserializing object of the `String` type for 'argument' failed." } // return type mismatch
        };

        [Theory]
        [MemberData(nameof(InvalidPayloads))]
        public void ParserThrowsForInvalidMessages(byte[] payload, string expectedExceptionMessage)
        {
            var payloadSize = payload.Length;
            Debug.Assert(payloadSize <= 0xff, "This test does not support payloads larger than 255");

            // prefix payload with the size
            var buffer = new byte[8 + payloadSize];
            buffer[7] = (byte)(payloadSize & 0xff);
            Array.Copy(payload, 0, buffer, 8, payloadSize);

            var binder = new TestBinder(new[] { typeof(string) }, typeof(string));
            var exception = Assert.Throws<FormatException>(() =>
                _hubProtocol.TryParseMessages(new ReadOnlySpan<byte>(buffer), binder, out var messages));

            Assert.Equal(expectedExceptionMessage, exception.Message);
        }

        [Theory]
        [InlineData(new object[] { new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x05, 0x01 }, 0 })]
        [InlineData(new object[] {
            new byte[]
            {
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x05, 0x03, 0xa1, 0x78, 0xa1, 0x45,
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x05, 0x03, 0xa1, 0x78, 0xa1, 0x45,
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x05, 0x03, 0xa1
            }, 2 })]
        public void ParserDoesNotConsumePartialData(byte[] payload, int expectedMessagesCount)
        {
            var binder = new TestBinder(new[] { typeof(string) }, typeof(string));
            var result = _hubProtocol.TryParseMessages(payload, binder, out var messages);
            Assert.True(result || messages.Count == 0);
            Assert.Equal(expectedMessagesCount, messages.Count);
        }
    }
}
