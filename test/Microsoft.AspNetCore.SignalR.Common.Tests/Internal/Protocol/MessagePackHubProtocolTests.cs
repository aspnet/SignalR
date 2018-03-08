// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.SignalR.Internal.Formatters;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Xunit;

namespace Microsoft.AspNetCore.SignalR.Common.Tests.Internal.Protocol
{
    using static HubMessageHelpers;

    public class MessagePackHubProtocolTests
    {
        private static readonly IDictionary<string, string> TestHeaders = new Dictionary<string, string>
        {
            { "Foo", "Bar" },
            { "KeyWith\nNew\r\nLines", "Still Works" },
            { "ValueWithNewLines", "Also\nWorks\r\nFine" },
        };

        private static readonly MessagePackHubProtocol _hubProtocol
            = new MessagePackHubProtocol();

        public enum TestEnum
        {
            Zero = 0,
            One
        }

        // Test Data for Parse/WriteMessages:
        // * Name: A string name that is used when reporting the test (it's the ToString value for ProtocolTestData)
        // * Message: The HubMessage that is either expected (in Parse) or used as input (in Write)
        // * Encoded: Raw MessagePackObject values (using the MessagePackHelpers static "Arr" and "Map" helpers) describing the message
        // * Binary: Base64-encoded binary "baseline" to sanity-check MsgPack-Cli behavior
        //
        // The Encoded value is used as input to "Parse" and as the expected output that is verified in "Write". So if our encoding changes,
        // those values will change and the Assert will give you a useful error telling you how the MsgPack structure itself changed (rather than just
        // a bunch of random bytes). However, we want to be sure MsgPack-Cli doesn't change behavior, so we also verify that the binary encoding
        // matches our expectation by comparing against a base64-string.
        //
        // If you change MsgPack encoding, you should update the 'encoded' values for these items, and then re-run the test. You'll get a failure which will
        // provide a new Base64 binary string to replace in the 'binary' value. Use a tool like https://sugendran.github.io/msgpack-visualizer/ to verify
        // that the MsgPack is correct and then just replace the Base64 value.

        public static IEnumerable<object[]> TestDataNames
        {
            get
            {
                foreach (var k in TestData.Keys)
                {
                    yield return new object[] { k };
                }
            }
        }

        public static IDictionary<string, ProtocolTestData> TestData => new[]
        {
            // Invocation messages
            new ProtocolTestData(
                name: "InvocationWithNoHeadersAndNoArgs",
                message: new InvocationMessage(invocationId: "xyz", target: "method", argumentBindingException: null),
                binary: "lQGAo3h5eqZtZXRob2SQ"),
            new ProtocolTestData(
                name: "InvocationWithNoHeadersNoIdAndNoArgs",
                message: new InvocationMessage(target: "method", argumentBindingException: null),
                binary: "lQGAwKZtZXRob2SQ"),
            new ProtocolTestData(
                name: "InvocationWithNoHeadersNoIdAndSingleNullArg",
                message: new InvocationMessage(target: "method", argumentBindingException: null, new object[] { null }),
                binary: "lQGAwKZtZXRob2SRwA=="),
            new ProtocolTestData(
                name: "InvocationWithNoHeadersNoIdAndSingleIntArg",
                message: new InvocationMessage(target: "method", argumentBindingException: null, 42),
                binary: "lQGAwKZtZXRob2SRKg=="),
            new ProtocolTestData(
                name: "InvocationWithNoHeadersNoIdIntAndStringArgs",
                message: new InvocationMessage(target: "method", argumentBindingException: null, 42, "string"),
                binary: "lQGAwKZtZXRob2SSKqZzdHJpbmc="),
            new ProtocolTestData(
                name: "InvocationWithNoHeadersNoIdIntAndEnumArgs",
                message: new InvocationMessage(target: "method", argumentBindingException: null, 42, TestEnum.One),
                binary: "lQGAwKZtZXRob2SSKqNPbmU="),
            new ProtocolTestData(
                name: "InvocationWithNoHeadersNoIdAndCustomObjectArg",
                message: new InvocationMessage(target: "method", argumentBindingException: null, 42, "string", new CustomObject()),
                binary: "lQGAwKZtZXRob2STKqZzdHJpbmeGqlN0cmluZ1Byb3CoU2lnbmFsUiGqRG91YmxlUHJvcMtAGSH7VELPEqdJbnRQcm9wKqxEYXRlVGltZVByb3DPCNSAbbJ2wACoTnVsbFByb3DAq0J5dGVBcnJQcm9wxAMBAgM="),
            new ProtocolTestData(
                name: "InvocationWithNoHeadersNoIdAndArrayOfCustomObjectArgs",
                message: new InvocationMessage(target: "method", argumentBindingException: null, new[] { new CustomObject(), new CustomObject() }),
                binary: "lQGAwKZtZXRob2SShqpTdHJpbmdQcm9wqFNpZ25hbFIhqkRvdWJsZVByb3DLQBkh+1RCzxKnSW50UHJvcCqsRGF0ZVRpbWVQcm9wzwjUgG2ydsAAqE51bGxQcm9wwKtCeXRlQXJyUHJvcMQDAQIDhqpTdHJpbmdQcm9wqFNpZ25hbFIhqkRvdWJsZVByb3DLQBkh+1RCzxKnSW50UHJvcCqsRGF0ZVRpbWVQcm9wzwjUgG2ydsAAqE51bGxQcm9wwKtCeXRlQXJyUHJvcMQDAQID"),
            new ProtocolTestData(
                name: "InvocationWithHeadersNoIdAndArrayOfCustomObjectArgs",
                message: AddHeaders(TestHeaders, new InvocationMessage(target: "method", argumentBindingException: null, new[] { new CustomObject(), new CustomObject() })),
                binary: "lQGDo0Zvb6NCYXKyS2V5V2l0aApOZXcNCkxpbmVzq1N0aWxsIFdvcmtzsVZhbHVlV2l0aE5ld0xpbmVzsEFsc28KV29ya3MNCkZpbmXApm1ldGhvZJKGqlN0cmluZ1Byb3CoU2lnbmFsUiGqRG91YmxlUHJvcMtAGSH7VELPEqdJbnRQcm9wKqxEYXRlVGltZVByb3DPCNSAbbJ2wACoTnVsbFByb3DAq0J5dGVBcnJQcm9wxAMBAgOGqlN0cmluZ1Byb3CoU2lnbmFsUiGqRG91YmxlUHJvcMtAGSH7VELPEqdJbnRQcm9wKqxEYXRlVGltZVByb3DPCNSAbbJ2wACoTnVsbFByb3DAq0J5dGVBcnJQcm9wxAMBAgM="),

            // StreamItem Messages
            new ProtocolTestData(
                name: "StreamItemWithNoHeadersAndNullItem",
                message: new StreamItemMessage(invocationId: "xyz", item: null),
                binary: "lAKAo3h5esA="),
            new ProtocolTestData(
                name: "StreamItemWithNoHeadersAndIntItem",
                message: new StreamItemMessage(invocationId: "xyz", item: 42),
                binary: "lAKAo3h5eio="),
            new ProtocolTestData(
                name: "StreamItemWithNoHeadersAndFloatItem",
                message: new StreamItemMessage(invocationId: "xyz", item: 42.0f),
                binary: "lAKAo3h5espCKAAA"),
            new ProtocolTestData(
                name: "StreamItemWithNoHeadersAndStringItem",
                message: new StreamItemMessage(invocationId: "xyz", item: "string"),
                binary: "lAKAo3h5eqZzdHJpbmc="),
            new ProtocolTestData(
                name: "StreamItemWithNoHeadersAndBoolItem",
                message: new StreamItemMessage(invocationId: "xyz", item: true),
                binary: "lAKAo3h5esM="),
            new ProtocolTestData(
                name: "StreamItemWithNoHeadersAndEnumItem",
                message: new StreamItemMessage(invocationId: "xyz", item: TestEnum.One),
                binary: "lAKAo3h5eqNPbmU="),
            new ProtocolTestData(
                name: "StreamItemWithNoHeadersAndCustomObjectItem",
                message: new StreamItemMessage(invocationId: "xyz", item: new CustomObject()),
                binary: "lAKAo3h5eoaqU3RyaW5nUHJvcKhTaWduYWxSIapEb3VibGVQcm9wy0AZIftUQs8Sp0ludFByb3AqrERhdGVUaW1lUHJvcM8I1IBtsnbAAKhOdWxsUHJvcMCrQnl0ZUFyclByb3DEAwECAw=="),
            new ProtocolTestData(
                name: "StreamItemWithNoHeadersAndCustomObjectArrayItem",
                message: new StreamItemMessage(invocationId: "xyz", item: new[] { new CustomObject(), new CustomObject() }),
                binary: "lAKAo3h5epKGqlN0cmluZ1Byb3CoU2lnbmFsUiGqRG91YmxlUHJvcMtAGSH7VELPEqdJbnRQcm9wKqxEYXRlVGltZVByb3DPCNSAbbJ2wACoTnVsbFByb3DAq0J5dGVBcnJQcm9wxAMBAgOGqlN0cmluZ1Byb3CoU2lnbmFsUiGqRG91YmxlUHJvcMtAGSH7VELPEqdJbnRQcm9wKqxEYXRlVGltZVByb3DPCNSAbbJ2wACoTnVsbFByb3DAq0J5dGVBcnJQcm9wxAMBAgM="),
            new ProtocolTestData(
                name: "StreamItemWithHeadersAndCustomObjectArrayItem",
                message: AddHeaders(TestHeaders, new StreamItemMessage(invocationId: "xyz", item: new[] { new CustomObject(), new CustomObject() })),
                binary: "lAKDo0Zvb6NCYXKyS2V5V2l0aApOZXcNCkxpbmVzq1N0aWxsIFdvcmtzsVZhbHVlV2l0aE5ld0xpbmVzsEFsc28KV29ya3MNCkZpbmWjeHl6koaqU3RyaW5nUHJvcKhTaWduYWxSIapEb3VibGVQcm9wy0AZIftUQs8Sp0ludFByb3AqrERhdGVUaW1lUHJvcM8I1IBtsnbAAKhOdWxsUHJvcMCrQnl0ZUFyclByb3DEAwECA4aqU3RyaW5nUHJvcKhTaWduYWxSIapEb3VibGVQcm9wy0AZIftUQs8Sp0ludFByb3AqrERhdGVUaW1lUHJvcM8I1IBtsnbAAKhOdWxsUHJvcMCrQnl0ZUFyclByb3DEAwECAw=="),

            // Completion Messages
            new ProtocolTestData(
                name: "CompletionWithNoHeadersAndError",
                message: CompletionMessage.WithError(invocationId: "xyz", error: "Error not found!"),
                binary: "lQOAo3h5egGwRXJyb3Igbm90IGZvdW5kIQ=="),
            new ProtocolTestData(
                name: "CompletionWithHeadersAndError",
                message: AddHeaders(TestHeaders, CompletionMessage.WithError(invocationId: "xyz", error: "Error not found!")),
                binary: "lQODo0Zvb6NCYXKyS2V5V2l0aApOZXcNCkxpbmVzq1N0aWxsIFdvcmtzsVZhbHVlV2l0aE5ld0xpbmVzsEFsc28KV29ya3MNCkZpbmWjeHl6AbBFcnJvciBub3QgZm91bmQh"),
            new ProtocolTestData(
                name: "CompletionWithNoHeadersAndNoResult",
                message: CompletionMessage.Empty(invocationId: "xyz"),
                binary: "lAOAo3h5egI="),
            new ProtocolTestData(
                name: "CompletionWithHeadersAndNoResult",
                message: AddHeaders(TestHeaders, CompletionMessage.Empty(invocationId: "xyz")),
                binary: "lAODo0Zvb6NCYXKyS2V5V2l0aApOZXcNCkxpbmVzq1N0aWxsIFdvcmtzsVZhbHVlV2l0aE5ld0xpbmVzsEFsc28KV29ya3MNCkZpbmWjeHl6Ag=="),
            new ProtocolTestData(
                name: "CompletionWithNoHeadersAndNullResult",
                message: CompletionMessage.WithResult(invocationId: "xyz", payload: null),
                binary: "lQOAo3h5egPA"),
            new ProtocolTestData(
                name: "CompletionWithNoHeadersAndIntResult",
                message: CompletionMessage.WithResult(invocationId: "xyz", payload: 42),
                binary: "lQOAo3h5egMq"),
            new ProtocolTestData(
                name: "CompletionWithNoHeadersAndEnumResult",
                message: CompletionMessage.WithResult(invocationId: "xyz", payload: TestEnum.One),
                binary: "lQOAo3h5egOjT25l"),
            new ProtocolTestData(
                name: "CompletionWithNoHeadersAndFloatResult",
                message: CompletionMessage.WithResult(invocationId: "xyz", payload: 42.0f),
                binary: "lQOAo3h5egPKQigAAA=="),
            new ProtocolTestData(
                name: "CompletionWithNoHeadersAndStringResult",
                message: CompletionMessage.WithResult(invocationId: "xyz", payload: "string"),
                binary: "lQOAo3h5egOmc3RyaW5n"),
            new ProtocolTestData(
                name: "CompletionWithNoHeadersAndBooleanResult",
                message: CompletionMessage.WithResult(invocationId: "xyz", payload: true),
                binary: "lQOAo3h5egPD"),
            new ProtocolTestData(
                name: "CompletionWithNoHeadersAndCustomObjectResult",
                message: CompletionMessage.WithResult(invocationId: "xyz", payload: new CustomObject()),
                binary: "lQOAo3h5egOGqlN0cmluZ1Byb3CoU2lnbmFsUiGqRG91YmxlUHJvcMtAGSH7VELPEqdJbnRQcm9wKqxEYXRlVGltZVByb3DPCNSAbbJ2wACoTnVsbFByb3DAq0J5dGVBcnJQcm9wxAMBAgM="),
            new ProtocolTestData(
                name: "CompletionWithNoHeadersAndCustomObjectArrayResult",
                message: CompletionMessage.WithResult(invocationId: "xyz", payload: new[] { new CustomObject(), new CustomObject() }),
                binary: "lQOAo3h5egOShqpTdHJpbmdQcm9wqFNpZ25hbFIhqkRvdWJsZVByb3DLQBkh+1RCzxKnSW50UHJvcCqsRGF0ZVRpbWVQcm9wzwjUgG2ydsAAqE51bGxQcm9wwKtCeXRlQXJyUHJvcMQDAQIDhqpTdHJpbmdQcm9wqFNpZ25hbFIhqkRvdWJsZVByb3DLQBkh+1RCzxKnSW50UHJvcCqsRGF0ZVRpbWVQcm9wzwjUgG2ydsAAqE51bGxQcm9wwKtCeXRlQXJyUHJvcMQDAQID"),
            new ProtocolTestData(
                name: "CompletionWithHeadersAndCustomObjectArrayResult",
                message: AddHeaders(TestHeaders, CompletionMessage.WithResult(invocationId: "xyz", payload: new[] { new CustomObject(), new CustomObject() })),
                binary: "lQODo0Zvb6NCYXKyS2V5V2l0aApOZXcNCkxpbmVzq1N0aWxsIFdvcmtzsVZhbHVlV2l0aE5ld0xpbmVzsEFsc28KV29ya3MNCkZpbmWjeHl6A5KGqlN0cmluZ1Byb3CoU2lnbmFsUiGqRG91YmxlUHJvcMtAGSH7VELPEqdJbnRQcm9wKqxEYXRlVGltZVByb3DPCNSAbbJ2wACoTnVsbFByb3DAq0J5dGVBcnJQcm9wxAMBAgOGqlN0cmluZ1Byb3CoU2lnbmFsUiGqRG91YmxlUHJvcMtAGSH7VELPEqdJbnRQcm9wKqxEYXRlVGltZVByb3DPCNSAbbJ2wACoTnVsbFByb3DAq0J5dGVBcnJQcm9wxAMBAgM="),

            // StreamInvocation Messages
            new ProtocolTestData(
                name: "StreamInvocationWithNoHeadersAndNoArgs",
                message: new StreamInvocationMessage(invocationId: "xyz", target: "method", argumentBindingException: null),
                binary: "lQSAo3h5eqZtZXRob2SQ"),
            new ProtocolTestData(
                name: "StreamInvocationWithNoHeadersAndNullArg",
                message: new StreamInvocationMessage(invocationId: "xyz", target: "method", argumentBindingException: null, new object[] { null }),
                binary: "lQSAo3h5eqZtZXRob2SRwA=="),
            new ProtocolTestData(
                name: "StreamInvocationWithNoHeadersAndIntArg",
                message: new StreamInvocationMessage(invocationId: "xyz", target: "method", argumentBindingException: null, 42),
                binary: "lQSAo3h5eqZtZXRob2SRKg=="),
            new ProtocolTestData(
                name: "StreamInvocationWithNoHeadersAndEnumArg",
                message: new StreamInvocationMessage(invocationId: "xyz", target: "method", argumentBindingException: null, TestEnum.One),
                binary: "lQSAo3h5eqZtZXRob2SRo09uZQ=="),
            new ProtocolTestData(
                name: "StreamInvocationWithNoHeadersAndIntAndStringArgs",
                message: new StreamInvocationMessage(invocationId: "xyz", target: "method", argumentBindingException: null, 42, "string"),
                binary: "lQSAo3h5eqZtZXRob2SSKqZzdHJpbmc="),
            new ProtocolTestData(
                name: "StreamInvocationWithNoHeadersAndIntStringAndCustomObjectArgs",
                message: new StreamInvocationMessage(invocationId: "xyz", target: "method", argumentBindingException: null, 42, "string", new CustomObject()),
                binary: "lQSAo3h5eqZtZXRob2STKqZzdHJpbmeGqlN0cmluZ1Byb3CoU2lnbmFsUiGqRG91YmxlUHJvcMtAGSH7VELPEqdJbnRQcm9wKqxEYXRlVGltZVByb3DPCNSAbbJ2wACoTnVsbFByb3DAq0J5dGVBcnJQcm9wxAMBAgM="),
            new ProtocolTestData(
                name: "StreamInvocationWithNoHeadersAndCustomObjectArrayArg",
                message: new StreamInvocationMessage(invocationId: "xyz", target: "method", argumentBindingException: null, new[] { new CustomObject(), new CustomObject() }),
                binary: "lQSAo3h5eqZtZXRob2SShqpTdHJpbmdQcm9wqFNpZ25hbFIhqkRvdWJsZVByb3DLQBkh+1RCzxKnSW50UHJvcCqsRGF0ZVRpbWVQcm9wzwjUgG2ydsAAqE51bGxQcm9wwKtCeXRlQXJyUHJvcMQDAQIDhqpTdHJpbmdQcm9wqFNpZ25hbFIhqkRvdWJsZVByb3DLQBkh+1RCzxKnSW50UHJvcCqsRGF0ZVRpbWVQcm9wzwjUgG2ydsAAqE51bGxQcm9wwKtCeXRlQXJyUHJvcMQDAQID"),
            new ProtocolTestData(
                name: "StreamInvocationWithHeadersAndCustomObjectArrayArg",
                message: AddHeaders(TestHeaders, new StreamInvocationMessage(invocationId: "xyz", target: "method", argumentBindingException: null, new[] { new CustomObject(), new CustomObject() })),
                binary: "lQSDo0Zvb6NCYXKyS2V5V2l0aApOZXcNCkxpbmVzq1N0aWxsIFdvcmtzsVZhbHVlV2l0aE5ld0xpbmVzsEFsc28KV29ya3MNCkZpbmWjeHl6pm1ldGhvZJKGqlN0cmluZ1Byb3CoU2lnbmFsUiGqRG91YmxlUHJvcMtAGSH7VELPEqdJbnRQcm9wKqxEYXRlVGltZVByb3DPCNSAbbJ2wACoTnVsbFByb3DAq0J5dGVBcnJQcm9wxAMBAgOGqlN0cmluZ1Byb3CoU2lnbmFsUiGqRG91YmxlUHJvcMtAGSH7VELPEqdJbnRQcm9wKqxEYXRlVGltZVByb3DPCNSAbbJ2wACoTnVsbFByb3DAq0J5dGVBcnJQcm9wxAMBAgM="),

            // CancelInvocation Messages
            new ProtocolTestData(
                name: "CancelInvocationWithNoHeaders",
                message: new CancelInvocationMessage(invocationId: "xyz"),
                binary: "kwWAo3h5eg=="),
            new ProtocolTestData(
                name: "CancelInvocationWithHeaders",
                message: AddHeaders(TestHeaders, new CancelInvocationMessage(invocationId: "xyz")),
                binary: "kwWDo0Zvb6NCYXKyS2V5V2l0aApOZXcNCkxpbmVzq1N0aWxsIFdvcmtzsVZhbHVlV2l0aE5ld0xpbmVzsEFsc28KV29ya3MNCkZpbmWjeHl6"),

            // Ping Messages
            new ProtocolTestData(
                name: "Ping",
                message: PingMessage.Instance,
                binary: "kQY="),
        }.ToDictionary(t => t.Name);

        [Theory]
        [MemberData(nameof(TestDataNames))]
        public void ParseMessages(string testDataName)
        {
            var testData = TestData[testDataName];

            // Verify that the input binary string decodes to the expected MsgPack primitives
            var bytes = Convert.FromBase64String(testData.Binary);

            // Parse the input fully now.
            bytes = Frame(bytes);
            var messages = new List<HubMessage>();
            Assert.True(_hubProtocol.TryParseMessages(bytes, new TestBinder(testData.Message), messages));

            Assert.Single(messages);
            Assert.Equal(testData.Message, messages[0], TestHubMessageEqualityComparer.Instance);
        }

        [Fact]
        public void ParseMessageWithExtraData()
        {
            var expectedMessage = new InvocationMessage(invocationId: "xyz", target: "method", argumentBindingException: null);

            // Verify that the input binary string decodes to the expected MsgPack primitives
            //Array(HubProtocolConstants.InvocationMessageType, Map(), "xyz", "method", Array(), "ex");
            var bytes = new byte[] { 0x96, 0x01, 0x80, 0xa3, 0x78, 0x79, 0x7a, 0xa6, 0x6d, 0x65, 0x74, 0x68, 0x6f, 0x64, 0x90, 0xa2, 0x65, 0x78 };

            // Parse the input fully now.
            bytes = Frame(bytes);
            var messages = new List<HubMessage>();
            Assert.True(_hubProtocol.TryParseMessages(bytes, new TestBinder(expectedMessage), messages));

            Assert.Single(messages);
            Assert.Equal(expectedMessage, messages[0], TestHubMessageEqualityComparer.Instance);
        }

        [Theory]
        [MemberData(nameof(TestDataNames))]
        public void WriteMessages(string testDataName)
        {
            var testData = TestData[testDataName];

            var bytes = Write(testData.Message);

            // Unframe the message to check the binary encoding
            ReadOnlyMemory<byte> byteSpan = bytes;
            Assert.True(BinaryMessageParser.TryParseMessage(ref byteSpan, out var unframed));

            // Check the baseline binary encoding, use Assert.True in order to configure the error message
            var actual = Convert.ToBase64String(unframed.ToArray());
            Assert.True(string.Equals(actual, testData.Binary, StringComparison.Ordinal), $"Binary encoding changed from{Environment.NewLine} [{testData.Binary}]{Environment.NewLine} to{Environment.NewLine} [{actual}]{Environment.NewLine}Please verify the MsgPack output and update the baseline");
        }

        public static IEnumerable<object[]> InvalidPayloads => new[]
        {
            // Message Type
            new object[] { new InvalidMessageData("MessageTypeString", new byte[] { 0x91, 0xa3, 0x66, 0x6f, 0x6f }, "Reading 'messageType' as Int32 failed.") },

            // Headers
            new object[] { new InvalidMessageData("HeadersNotAMap", new byte[] { 0x92, 0x01, 0xa3, 0x66, 0x6f, 0x6f }, "Reading map length for 'headers' failed.") },
            new object[] { new InvalidMessageData("HeaderKeyInt", new byte[] { 0x92, 0x01, 0x82, 0x2a, 0xa3, 0x66, 0x6f, 0x6f }, "Reading 'headers[0].Key' as String failed.") },
            new object[] { new InvalidMessageData("HeaderValueInt", new byte[] { 0x92, 0x01, 0x82, 0xa3, 0x66, 0x6f, 0x6f, 0x2a }, "Reading 'headers[0].Value' as String failed.") },
            new object[] { new InvalidMessageData("HeaderKeyArray", new byte[] { 0x92, 0x01, 0x84, 0xa3, 0x66, 0x6f, 0x6f, 0xa3, 0x66, 0x6f, 0x6f, 0x90, 0xa3, 0x66, 0x6f, 0x6f }, "Reading 'headers[1].Key' as String failed.") },
            new object[] { new InvalidMessageData("HeaderValueArray", new byte[] { 0x92, 0x01, 0x84, 0xa3, 0x66, 0x6f, 0x6f, 0xa3, 0x66, 0x6f, 0x6f, 0xa3, 0x66, 0x6f, 0x6f, 0x90 }, "Reading 'headers[1].Value' as String failed.") },

            // InvocationMessage
            new object[] { new InvalidMessageData("InvocationMissingId", new byte[] { 0x92, 0x01, 0x80 }, "Reading 'invocationId' as String failed.") },
            new object[] { new InvalidMessageData("InvocationIdBoolean", new byte[] { 0x91, 0x01, 0x80, 0xc2 }, "Reading 'invocationId' as String failed.") },
            new object[] { new InvalidMessageData("InvocationTargetMissing", new byte[] { 0x93, 0x01, 0x80, 0xa3, 0x61, 0x62, 0x63 }, "Reading 'target' as String failed.") },
            new object[] { new InvalidMessageData("InvocationTargetInt", new byte[] { 0x94, 0x01, 0x80, 0xa3, 0x61, 0x62, 0x63, 0x2a }, "Reading 'target' as String failed.") },

            // StreamInvocationMessage
            new object[] { new InvalidMessageData("StreamInvocationMissingId", new byte[] { 0x92, 0x04, 0x80 }, "Reading 'invocationId' as String failed.") },
            new object[] { new InvalidMessageData("StreamInvocationIdBoolean", new byte[] { 0x93, 0x04, 0x80, 0xc2 }, "Reading 'invocationId' as String failed.") },
            new object[] { new InvalidMessageData("StreamInvocationTargetMissing", new byte[] { 0x93, 0x04, 0x80, 0xa3, 0x61, 0x62, 0x63 }, "Reading 'target' as String failed.") },
            new object[] { new InvalidMessageData("StreamInvocationTargetInt", new byte[] { 0x94, 0x04, 0x80, 0xa3, 0x61, 0x62, 0x63, 0x2a }, "Reading 'target' as String failed.") },

            // StreamItemMessage
            new object[] { new InvalidMessageData("StreamItemMissingId", new byte[] { 0x92, 0x02, 0x80 }, "Reading 'invocationId' as String failed.") },
            new object[] { new InvalidMessageData("StreamItemInvocationIdBoolean", new byte[] { 0x93, 0x02, 0x80, 0xc2 }, "Reading 'invocationId' as String failed.") },
            new object[] { new InvalidMessageData("StreamItemMissing", new byte[] { 0x93, 0x02, 0x80, 0xa3, 0x78, 0x79, 0x7a }, "Deserializing object of the `String` type for 'item' failed.") },
            new object[] { new InvalidMessageData("StreamItemTypeMismatch", new byte[] { 0x94, 0x02, 0x80, 0xa3, 0x78, 0x79, 0x7a, 0x2a }, "Deserializing object of the `String` type for 'item' failed.") },

            // CompletionMessage
            new object[] { new InvalidMessageData("CompletionMissingId", new byte[] { 0x92, 0x03, 0x80 }, "Reading 'invocationId' as String failed.") },
            new object[] { new InvalidMessageData("CompletionIdBoolean", new byte[] { 0x93, 0x03, 0x80, 0xc2 }, "Reading 'invocationId' as String failed.") },
            new object[] { new InvalidMessageData("CompletionResultKindString", new byte[] { 0x94, 0x03, 0x80, 0xa3, 0x78, 0x79, 0x7a, 0xa3, 0x61, 0x62, 0x63 }, "Reading 'resultKind' as Int32 failed.") },
            new object[] { new InvalidMessageData("CompletionResultKindOutOfRange", new byte[] { 0x94, 0x03, 0x80, 0xa3, 0x78, 0x79, 0x7a, 0x2a }, "Invalid invocation result kind.") },
            new object[] { new InvalidMessageData("CompletionErrorMissing", new byte[] { 0x94, 0x03, 0x80, 0xa3, 0x78, 0x79, 0x7a, 0x01 }, "Reading 'error' as String failed.") },
            new object[] { new InvalidMessageData("CompletionErrorInt", new byte[] { 0x95, 0x03, 0x80, 0xa3, 0x78, 0x79, 0x7a, 0x01, 0x2a }, "Reading 'error' as String failed.") },
            new object[] { new InvalidMessageData("CompletionResultMissing", new byte[] { 0x94, 0x03, 0x80, 0xa3, 0x78, 0x79, 0x7a, 0x03 }, "Deserializing object of the `String` type for 'argument' failed.") },
            new object[] { new InvalidMessageData("CompletionResultTypeMismatch", new byte[] { 0x95, 0x03, 0x80, 0xa3, 0x78, 0x79, 0x7a, 0x03, 0x2a }, "Deserializing object of the `String` type for 'argument' failed.") },
        };

        [Theory]
        [MemberData(nameof(InvalidPayloads))]
        public void ParserThrowsForInvalidMessages(InvalidMessageData testData)
        {
            var buffer = Frame(testData.Encoded);
            var binder = new TestBinder(new[] { typeof(string) }, typeof(string));
            var messages = new List<HubMessage>();
            var exception = Assert.Throws<FormatException>(() => _hubProtocol.TryParseMessages(buffer, binder, messages));

            Assert.Equal(testData.ErrorMessage, exception.Message);
        }

        public static IEnumerable<object[]> ArgumentBindingErrors => new[]
        {
            // InvocationMessage
            new object[] {new InvalidMessageData("InvocationArgumentArrayMissing", new byte[] { 0x94, 0x01, 0x80, 0xa3, 0x61, 0x62, 0x63, 0xa3, 0x78, 0x79, 0x7a }, "Reading array length for 'arguments' failed.") },
            new object[] {new InvalidMessageData("InvocationArgumentArrayNotAnArray", new byte[] { 0x95, 0x01, 0x80, 0xa3, 0x61, 0x62, 0x63, 0xa3, 0x78, 0x79, 0x7a, 0x2a }, "Reading array length for 'arguments' failed.") },
            new object[] {new InvalidMessageData("InvocationArgumentArraySizeMismatchEmpty", new byte[] { 0x95, 0x01, 0x80, 0xa3, 0x61, 0x62, 0x63, 0xa3, 0x78, 0x79, 0x7a, 0x90 }, "Invocation provides 0 argument(s) but target expects 1.") },
            new object[] {new InvalidMessageData("InvocationArgumentArraySizeMismatchTooLarge", new byte[] { 0x95, 0x01, 0x80, 0xa3, 0x61, 0x62, 0x63, 0xa3, 0x78, 0x79, 0x7a, 0x92, 0xa1, 0x61, 0xa1, 0x62 }, "Invocation provides 2 argument(s) but target expects 1.") },
            new object[] {new InvalidMessageData("InvocationArgumentTypeMismatch", new byte[] { 0x95, 0x01, 0x80, 0xa3, 0x61, 0x62, 0x63, 0xa3, 0x78, 0x79, 0x7a, 0x91, 0x2a }, "Error binding arguments. Make sure that the types of the provided values match the types of the hub method being invoked.") },

            // StreamInvocationMessage
            new object[] {new InvalidMessageData("StreamInvocationArgumentArrayMissing", new byte[] { 0x94, 0x04, 0x80, 0xa3, 0x61, 0x62, 0x63, 0xa3, 0x78, 0x79, 0x7a }, "Reading array length for 'arguments' failed.") }, // array is missing
            new object[] {new InvalidMessageData("StreamInvocationArgumentArrayNotAnArray", new byte[] { 0x95, 0x04, 0x80, 0xa3, 0x61, 0x62, 0x63, 0xa3, 0x78, 0x79, 0x7a, 0x2a }, "Reading array length for 'arguments' failed.") }, // arguments isn't an array
            new object[] {new InvalidMessageData("StreamInvocationArgumentArraySizeMismatchEmpty", new byte[] { 0x95, 0x04, 0x80, 0xa3, 0x61, 0x62, 0x63, 0xa3, 0x78, 0x79, 0x7a, 0x90 }, "Invocation provides 0 argument(s) but target expects 1.") }, // array is missing elements
            new object[] {new InvalidMessageData("StreamInvocationArgumentArraySizeMismatchTooLarge", new byte[] { 0x95, 0x04, 0x80, 0xa3, 0x61, 0x62, 0x63, 0xa3, 0x78, 0x79, 0x7a, 0x92, 0xa1, 0x61, 0xa1, 0x62 }, "Invocation provides 2 argument(s) but target expects 1.") }, // argument count does not match binder argument count
            new object[] {new InvalidMessageData("StreamInvocationArgumentTypeMismatch", new byte[] { 0x95, 0x04, 0x80, 0xa3, 0x61, 0x62, 0x63, 0xa3, 0x78, 0x79, 0x7a, 0x91, 0x2a }, "Error binding arguments. Make sure that the types of the provided values match the types of the hub method being invoked.") }, // argument type mismatch
        };

        [Theory]
        [MemberData(nameof(ArgumentBindingErrors))]
        public void GettingArgumentsThrowsIfBindingFailed(InvalidMessageData testData)
        {
            var buffer = Frame(testData.Encoded);
            var binder = new TestBinder(new[] { typeof(string) }, typeof(string));
            var messages = new List<HubMessage>();
            _hubProtocol.TryParseMessages(buffer, binder, messages);
            var exception = Assert.Throws<FormatException>(() => ((HubMethodInvocationMessage)messages[0]).Arguments);

            Assert.Equal(testData.ErrorMessage, exception.Message);
        }

        [Theory]
        [InlineData(new object[] { new byte[] { 0x05, 0x01 }, 0 })]
        public void ParserDoesNotConsumePartialData(byte[] payload, int expectedMessagesCount)
        {
            var binder = new TestBinder(new[] { typeof(string) }, typeof(string));
            var messages = new List<HubMessage>();
            var result = _hubProtocol.TryParseMessages(payload, binder, messages);
            Assert.True(result || messages.Count == 0);
            Assert.Equal(expectedMessagesCount, messages.Count);
        }

        [Fact]
        public void SerializerCanSerializeTypesWithNoDefaultCtor()
        {
            var result = Write(CompletionMessage.WithResult("0", new List<int> { 42 }.AsReadOnly()));
            AssertMessages(new byte[] { 0x95, 0x03, 0x80, 0xa1, 0x30, 0x03, 0x91, 0x2a }, result);
        }

        private static void AssertMessages(byte[] expectedOutput, ReadOnlyMemory<byte> bytes)
        {
            Assert.True(BinaryMessageParser.TryParseMessage(ref bytes, out var message));
            Assert.Equal(expectedOutput, message.ToArray());
        }

        private static byte[] Frame(byte[] input)
        {
            using (var stream = new MemoryStream())
            {
                BinaryMessageFormatter.WriteLengthPrefix(input.Length, stream);
                stream.Write(input, 0, input.Length);
                return stream.ToArray();
            }
        }

        private static byte[] Write(HubMessage message)
        {
            using (var stream = new MemoryStream())
            {
                _hubProtocol.WriteMessage(message, stream);
                stream.Flush();
                return stream.ToArray();
            }
        }

        public class InvalidMessageData
        {
            public string Name { get; private set; }
            public byte[] Encoded { get; private set; }
            public string ErrorMessage { get; private set; }

            public InvalidMessageData(string name, byte[] encoded, string errorMessage)
            {
                Name = name;
                Encoded = encoded;
                ErrorMessage = errorMessage;
            }

            public override string ToString() => Name;
        }

        public class ProtocolTestData
        {
            public string Name { get; }
            public string Binary { get; }
            public HubMessage Message { get; }

            public ProtocolTestData(string name, HubMessage message, string binary)
            {
                Name = name;
                Message = message;
                Binary = binary;
            }

            public override string ToString() => Name;
        }
    }
}
