// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.AspNetCore.SignalR.Internal.Formatters;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using MsgPack;
using Xunit;
using Xunit.Abstractions;
using static Microsoft.AspNetCore.SignalR.Common.Tests.Internal.Protocol.MessagePackHelpers;

namespace Microsoft.AspNetCore.SignalR.Common.Tests.Internal.Protocol
{
    public class MessagePackHubProtocolTests
    {
        private static readonly IReadOnlyDictionary<string, string> TestHeaders = new Dictionary<string, string>
        {
            { "Foo", "Bar" },
            { "KeyWith\nNew\r\nLines", "Still Works" },
            { "ValueWithNewLines", "Also\nWorks\r\nFine" },
        };

        private static MessagePackObject TestHeadersSerialized = Map(
            ("Foo", "Bar"),
            ("KeyWith\nNew\r\nLines", "Still Works"),
            ("ValueWithNewLines", "Also\nWorks\r\nFine"));

        private static readonly MessagePackHubProtocol _hubProtocol
            = new MessagePackHubProtocol();

        private static MessagePackObject CustomObjectSerialized = Map(
            ("ByteArrProp", new MessagePackObject(new byte[] { 1, 2, 3 }, isBinary: true)),
            ("DateTimeProp", new DateTime(2017, 4, 11).Ticks),
            ("DoubleProp", 6.2831853071),
            ("IntProp", 42),
            ("NullProp", MessagePackObject.Nil),
            ("StringProp", "SignalR!"));

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
        public static IEnumerable<object[]> TestData => new[]
        {
            new object[] {
                new ProtocolTestData(
                    name: "InvocationWithNoHeadersAndNoArgs",
                    message: new InvocationMessage(HubMessage.EmptyHeaders, invocationId: "xyz", target: "method", argumentBindingException: null),
                    encoded: Arr(Map(), HubProtocolConstants.InvocationMessageType, "xyz", "method", Arr()),
                    binary: "lYABo3h5eqZtZXRob2SQ"),
            },

            // Invocation messages
            new object[] {
                new ProtocolTestData(
                    name: "InvocationWithNoHeadersNoIdAndNoArgs",
                    message: new InvocationMessage(HubMessage.EmptyHeaders, target: "method", argumentBindingException: null),
                    encoded: Arr(Map(), HubProtocolConstants.InvocationMessageType, MessagePackObject.Nil, "method", Arr()),
                    binary: "lYABwKZtZXRob2SQ"),
            },
            new object[] {
                new ProtocolTestData(
                    name: "InvocationWithNoHeadersNoIdAndSingleNullArg",
                    message: new InvocationMessage(HubMessage.EmptyHeaders, target: "method", argumentBindingException: null, new object[] { null }),
                    encoded: Arr(Map(), HubProtocolConstants.InvocationMessageType, MessagePackObject.Nil, "method", Arr(MessagePackObject.Nil)),
                    binary: "lYABwKZtZXRob2SRwA=="),
            },
            new object[] {
                new ProtocolTestData(
                    name: "InvocationWithNoHeadersNoIdAndSingleIntArg",
                    message: new InvocationMessage(HubMessage.EmptyHeaders, target: "method", argumentBindingException: null, 42),
                    encoded: Arr(Map(), HubProtocolConstants.InvocationMessageType, MessagePackObject.Nil, "method", Arr(42)),
                    binary: "lYABwKZtZXRob2SRKg=="),
            },
            new object[] {
                new ProtocolTestData(
                    name: "InvocationWithNoHeadersNoIdIntAndStringArgs",
                    message: new InvocationMessage(HubMessage.EmptyHeaders, target: "method", argumentBindingException: null, 42, "string"),
                    encoded: Arr(Map(), HubProtocolConstants.InvocationMessageType, MessagePackObject.Nil, "method", Arr(42, "string")),
                    binary: "lYABwKZtZXRob2SSKqZzdHJpbmc="),
            },
            new object[] {
                new ProtocolTestData(
                    name: "InvocationWithNoHeadersNoIdAndCustomObjectArg",
                    message: new InvocationMessage(HubMessage.EmptyHeaders, target: "method", argumentBindingException: null, 42, "string", new CustomObject()),
                    encoded: Arr(Map(), HubProtocolConstants.InvocationMessageType, MessagePackObject.Nil, "method", Arr(42, "string", CustomObjectSerialized)),
                    binary: "lYABwKZtZXRob2STKqZzdHJpbmeGq0J5dGVBcnJQcm9wxAMBAgOsRGF0ZVRpbWVQcm9w0wjUgG2ydsAAqkRvdWJsZVByb3DLQBkh+1RCzxKnSW50UHJvcCqoTnVsbFByb3DAqlN0cmluZ1Byb3CoU2lnbmFsUiE="),
            },
            new object[] {
                new ProtocolTestData(
                    name: "InvocationWithNoHeadersNoIdAndArrayOfCustomObjectArgs",
                    message: new InvocationMessage(HubMessage.EmptyHeaders, target: "method", argumentBindingException: null, new[] { new CustomObject(), new CustomObject() }),
                    encoded: Arr(Map(), HubProtocolConstants.InvocationMessageType, MessagePackObject.Nil, "method", Arr(CustomObjectSerialized, CustomObjectSerialized)),
                    binary: "lYABwKZtZXRob2SShqtCeXRlQXJyUHJvcMQDAQIDrERhdGVUaW1lUHJvcNMI1IBtsnbAAKpEb3VibGVQcm9wy0AZIftUQs8Sp0ludFByb3AqqE51bGxQcm9wwKpTdHJpbmdQcm9wqFNpZ25hbFIhhqtCeXRlQXJyUHJvcMQDAQIDrERhdGVUaW1lUHJvcNMI1IBtsnbAAKpEb3VibGVQcm9wy0AZIftUQs8Sp0ludFByb3AqqE51bGxQcm9wwKpTdHJpbmdQcm9wqFNpZ25hbFIh"),
            },
            new object[] {
                new ProtocolTestData(
                    name: "InvocationWithHeadersNoIdAndArrayOfCustomObjectArgs",
                    message: new InvocationMessage(TestHeaders, target: "method", argumentBindingException: null, new[] { new CustomObject(), new CustomObject() }),
                    encoded: Arr(TestHeadersSerialized, HubProtocolConstants.InvocationMessageType, MessagePackObject.Nil, "method", Arr(CustomObjectSerialized, CustomObjectSerialized)),
                    binary: "lYOjRm9vo0JhcrJLZXlXaXRoCk5ldw0KTGluZXOrU3RpbGwgV29ya3OxVmFsdWVXaXRoTmV3TGluZXOwQWxzbwpXb3Jrcw0KRmluZQHApm1ldGhvZJKGq0J5dGVBcnJQcm9wxAMBAgOsRGF0ZVRpbWVQcm9w0wjUgG2ydsAAqkRvdWJsZVByb3DLQBkh+1RCzxKnSW50UHJvcCqoTnVsbFByb3DAqlN0cmluZ1Byb3CoU2lnbmFsUiGGq0J5dGVBcnJQcm9wxAMBAgOsRGF0ZVRpbWVQcm9w0wjUgG2ydsAAqkRvdWJsZVByb3DLQBkh+1RCzxKnSW50UHJvcCqoTnVsbFByb3DAqlN0cmluZ1Byb3CoU2lnbmFsUiE="),
            },

            // StreamItem Messages
            new object[] {
                new ProtocolTestData(
                    name: "StreamItemWithNoHeadersAndNullItem",
                    message: new StreamItemMessage(HubMessage.EmptyHeaders, invocationId: "xyz", item: null),
                    encoded: Arr(Map(), HubProtocolConstants.StreamItemMessageType, "xyz", MessagePackObject.Nil),
                    binary: "lIACo3h5esA="),
            },
            new object[] {
                new ProtocolTestData(
                    name: "StreamItemWithNoHeadersAndIntItem",
                    message: new StreamItemMessage(HubMessage.EmptyHeaders, invocationId: "xyz", item: 42),
                    encoded: Arr(Map(), HubProtocolConstants.StreamItemMessageType, "xyz", 42),
                    binary: "lIACo3h5eio="),
            },
            new object[] {
                new ProtocolTestData(
                    name: "StreamItemWithNoHeadersAndFloatItem",
                    message: new StreamItemMessage(HubMessage.EmptyHeaders, invocationId: "xyz", item: 42.0f),
                    encoded: Arr(Map(), HubProtocolConstants.StreamItemMessageType, "xyz", 42.0f),
                    binary: "lIACo3h5espCKAAA"),
            },
            new object[] {
                new ProtocolTestData(
                    name: "StreamItemWithNoHeadersAndStringItem",
                    message: new StreamItemMessage(HubMessage.EmptyHeaders, invocationId: "xyz", item: "string"),
                    encoded: Arr(Map(), HubProtocolConstants.StreamItemMessageType, "xyz", "string"),
                    binary: "lIACo3h5eqZzdHJpbmc="),
            },
            new object[] {
                new ProtocolTestData(
                    name: "StreamItemWithNoHeadersAndBoolItem",
                    message: new StreamItemMessage(HubMessage.EmptyHeaders, invocationId: "xyz", item: true),
                    encoded: Arr(Map(), HubProtocolConstants.StreamItemMessageType, "xyz", true),
                    binary: "lIACo3h5esM="),
            },
            new object[] {
                new ProtocolTestData(
                    name: "StreamItemWithNoHeadersAndCustomObjectItem",
                    message: new StreamItemMessage(HubMessage.EmptyHeaders, invocationId: "xyz", item: new CustomObject()),
                    encoded: Arr(Map(), HubProtocolConstants.StreamItemMessageType, "xyz", CustomObjectSerialized),
                    binary: "lIACo3h5eoarQnl0ZUFyclByb3DEAwECA6xEYXRlVGltZVByb3DTCNSAbbJ2wACqRG91YmxlUHJvcMtAGSH7VELPEqdJbnRQcm9wKqhOdWxsUHJvcMCqU3RyaW5nUHJvcKhTaWduYWxSIQ=="),
            },
            new object[] {
                new ProtocolTestData(
                    name: "StreamItemWithNoHeadersAndCustomObjectArrayItem",
                    message: new StreamItemMessage(HubMessage.EmptyHeaders, invocationId: "xyz", item: new[] { new CustomObject(), new CustomObject() }),
                    encoded: Arr(Map(), HubProtocolConstants.StreamItemMessageType, "xyz", Arr(CustomObjectSerialized, CustomObjectSerialized)),
                    binary: "lIACo3h5epKGq0J5dGVBcnJQcm9wxAMBAgOsRGF0ZVRpbWVQcm9w0wjUgG2ydsAAqkRvdWJsZVByb3DLQBkh+1RCzxKnSW50UHJvcCqoTnVsbFByb3DAqlN0cmluZ1Byb3CoU2lnbmFsUiGGq0J5dGVBcnJQcm9wxAMBAgOsRGF0ZVRpbWVQcm9w0wjUgG2ydsAAqkRvdWJsZVByb3DLQBkh+1RCzxKnSW50UHJvcCqoTnVsbFByb3DAqlN0cmluZ1Byb3CoU2lnbmFsUiE="),
            },
            new object[] {
                new ProtocolTestData(
                    name: "StreamItemWithHeadersAndCustomObjectArrayItem",
                    message: new StreamItemMessage(TestHeaders, invocationId: "xyz", item: new[] { new CustomObject(), new CustomObject() }),
                    encoded: Arr(TestHeadersSerialized, HubProtocolConstants.StreamItemMessageType, "xyz", Arr(CustomObjectSerialized, CustomObjectSerialized)),
                    binary: "lIOjRm9vo0JhcrJLZXlXaXRoCk5ldw0KTGluZXOrU3RpbGwgV29ya3OxVmFsdWVXaXRoTmV3TGluZXOwQWxzbwpXb3Jrcw0KRmluZQKjeHl6koarQnl0ZUFyclByb3DEAwECA6xEYXRlVGltZVByb3DTCNSAbbJ2wACqRG91YmxlUHJvcMtAGSH7VELPEqdJbnRQcm9wKqhOdWxsUHJvcMCqU3RyaW5nUHJvcKhTaWduYWxSIYarQnl0ZUFyclByb3DEAwECA6xEYXRlVGltZVByb3DTCNSAbbJ2wACqRG91YmxlUHJvcMtAGSH7VELPEqdJbnRQcm9wKqhOdWxsUHJvcMCqU3RyaW5nUHJvcKhTaWduYWxSIQ=="),
            },

            // Completion Messages
            new object[] {
                new ProtocolTestData(
                    name: "CompletionWithNoHeadersAndError",
                    message: CompletionMessage.WithError(HubMessage.EmptyHeaders, invocationId: "xyz", error: "Error not found!"),
                    encoded: Arr(Map(), HubProtocolConstants.CompletionMessageType, "xyz", 1, "Error not found!"),
                    binary: "lYADo3h5egGwRXJyb3Igbm90IGZvdW5kIQ=="),
            },
            new object[] {
                new ProtocolTestData(
                    name: "CompletionWithHeadersAndError",
                    message: CompletionMessage.WithError(TestHeaders, invocationId: "xyz", error: "Error not found!"),
                    encoded: Arr(TestHeadersSerialized, HubProtocolConstants.CompletionMessageType, "xyz", 1, "Error not found!"),
                    binary: "lYOjRm9vo0JhcrJLZXlXaXRoCk5ldw0KTGluZXOrU3RpbGwgV29ya3OxVmFsdWVXaXRoTmV3TGluZXOwQWxzbwpXb3Jrcw0KRmluZQOjeHl6AbBFcnJvciBub3QgZm91bmQh"),
            },
            new object[] {
                new ProtocolTestData(
                    name: "CompletionWithNoHeadersAndNoResult",
                    message: CompletionMessage.Empty(HubMessage.EmptyHeaders, invocationId: "xyz"),
                    encoded: Arr(Map(), HubProtocolConstants.CompletionMessageType, "xyz", 2),
                    binary: "lIADo3h5egI="),
            },
            new object[] {
                new ProtocolTestData(
                    name: "CompletionWithHeadersAndNoResult",
                    message: CompletionMessage.Empty(TestHeaders, invocationId: "xyz"),
                    encoded: Arr(TestHeadersSerialized, HubProtocolConstants.CompletionMessageType, "xyz", 2),
                    binary: "lIOjRm9vo0JhcrJLZXlXaXRoCk5ldw0KTGluZXOrU3RpbGwgV29ya3OxVmFsdWVXaXRoTmV3TGluZXOwQWxzbwpXb3Jrcw0KRmluZQOjeHl6Ag=="),
            },
            new object[] {
                new ProtocolTestData(
                    name: "CompletionWithNoHeadersAndNullResult",
                    message: CompletionMessage.WithResult(HubMessage.EmptyHeaders, invocationId: "xyz", payload: null),
                    encoded: Arr(Map(), HubProtocolConstants.CompletionMessageType, "xyz", 3, MessagePackObject.Nil),
                    binary: "lYADo3h5egPA"),
            },
            new object[] {
                new ProtocolTestData(
                    name: "CompletionWithNoHeadersAndIntResult",
                    message: CompletionMessage.WithResult(HubMessage.EmptyHeaders, invocationId: "xyz", payload: 42),
                    encoded: Arr(Map(), HubProtocolConstants.CompletionMessageType, "xyz", 3, 42),
                    binary: "lYADo3h5egMq"),
            },
            new object[] {
                new ProtocolTestData(
                    name: "CompletionWithNoHeadersAndFloatResult",
                    message: CompletionMessage.WithResult(HubMessage.EmptyHeaders, invocationId: "xyz", payload: 42.0f),
                    encoded: Arr(Map(), HubProtocolConstants.CompletionMessageType, "xyz", 3, 42.0f),
                    binary: "lYADo3h5egPKQigAAA=="),
            },
            new object[] {
                new ProtocolTestData(
                    name: "CompletionWithNoHeadersAndStringResult",
                    message: CompletionMessage.WithResult(HubMessage.EmptyHeaders, invocationId: "xyz", payload: "string"),
                    encoded: Arr(Map(), HubProtocolConstants.CompletionMessageType, "xyz", 3, "string"),
                    binary: "lYADo3h5egOmc3RyaW5n"),
            },
            new object[] {
                new ProtocolTestData(
                    name: "CompletionWithNoHeadersAndBooleanResult",
                    message: CompletionMessage.WithResult(HubMessage.EmptyHeaders, invocationId: "xyz", payload: true),
                    encoded: Arr(Map(), HubProtocolConstants.CompletionMessageType, "xyz", 3, true),
                    binary: "lYADo3h5egPD"),
            },
            new object[] {
                new ProtocolTestData(
                    name: "CompletionWithNoHeadersAndCustomObjectResult",
                    message: CompletionMessage.WithResult(HubMessage.EmptyHeaders, invocationId: "xyz", payload: new CustomObject()),
                    encoded: Arr(Map(), HubProtocolConstants.CompletionMessageType, "xyz", 3, CustomObjectSerialized),
                    binary: "lYADo3h5egOGq0J5dGVBcnJQcm9wxAMBAgOsRGF0ZVRpbWVQcm9w0wjUgG2ydsAAqkRvdWJsZVByb3DLQBkh+1RCzxKnSW50UHJvcCqoTnVsbFByb3DAqlN0cmluZ1Byb3CoU2lnbmFsUiE="),
            },
            new object[] {
                new ProtocolTestData(
                    name: "CompletionWithNoHeadersAndCustomObjectArrayResult",
                    message: CompletionMessage.WithResult(HubMessage.EmptyHeaders, invocationId: "xyz", payload: new[] { new CustomObject(), new CustomObject() }),
                    encoded: Arr(Map(), HubProtocolConstants.CompletionMessageType, "xyz", 3, Arr(CustomObjectSerialized, CustomObjectSerialized)),
                    binary: "lYADo3h5egOShqtCeXRlQXJyUHJvcMQDAQIDrERhdGVUaW1lUHJvcNMI1IBtsnbAAKpEb3VibGVQcm9wy0AZIftUQs8Sp0ludFByb3AqqE51bGxQcm9wwKpTdHJpbmdQcm9wqFNpZ25hbFIhhqtCeXRlQXJyUHJvcMQDAQIDrERhdGVUaW1lUHJvcNMI1IBtsnbAAKpEb3VibGVQcm9wy0AZIftUQs8Sp0ludFByb3AqqE51bGxQcm9wwKpTdHJpbmdQcm9wqFNpZ25hbFIh"),
            },
            new object[] {
                new ProtocolTestData(
                    name: "CompletionWithHeadersAndCustomObjectArrayResult",
                    message: CompletionMessage.WithResult(TestHeaders, invocationId: "xyz", payload: new[] { new CustomObject(), new CustomObject() }),
                    encoded: Arr(TestHeadersSerialized, HubProtocolConstants.CompletionMessageType, "xyz", 3, Arr(CustomObjectSerialized, CustomObjectSerialized)),
                    binary: "lYOjRm9vo0JhcrJLZXlXaXRoCk5ldw0KTGluZXOrU3RpbGwgV29ya3OxVmFsdWVXaXRoTmV3TGluZXOwQWxzbwpXb3Jrcw0KRmluZQOjeHl6A5KGq0J5dGVBcnJQcm9wxAMBAgOsRGF0ZVRpbWVQcm9w0wjUgG2ydsAAqkRvdWJsZVByb3DLQBkh+1RCzxKnSW50UHJvcCqoTnVsbFByb3DAqlN0cmluZ1Byb3CoU2lnbmFsUiGGq0J5dGVBcnJQcm9wxAMBAgOsRGF0ZVRpbWVQcm9w0wjUgG2ydsAAqkRvdWJsZVByb3DLQBkh+1RCzxKnSW50UHJvcCqoTnVsbFByb3DAqlN0cmluZ1Byb3CoU2lnbmFsUiE="),
            },

            // StreamInvocation Messages
            new object[] {
                new ProtocolTestData(
                    name: "StreamInvocationWithNoHeadersAndNoArgs",
                    message: new StreamInvocationMessage(HubMessage.EmptyHeaders, invocationId: "xyz", target: "method", argumentBindingException: null),
                    encoded: Arr(Map(), HubProtocolConstants.StreamInvocationMessageType, "xyz", "method", Arr()),
                    binary: "lYAEo3h5eqZtZXRob2SQ"),
            },
            new object[] {
                new ProtocolTestData(
                    name: "StreamInvocationWithNoHeadersAndNullArg",
                    message: new StreamInvocationMessage(HubMessage.EmptyHeaders, invocationId: "xyz", target: "method", argumentBindingException: null, new object[] { null }),
                    encoded: Arr(Map(), HubProtocolConstants.StreamInvocationMessageType, "xyz", "method", Arr(MessagePackObject.Nil)),
                    binary: "lYAEo3h5eqZtZXRob2SRwA=="),
            },
            new object[] {
                new ProtocolTestData(
                    name: "StreamInvocationWithNoHeadersAndIntArg",
                    message: new StreamInvocationMessage(HubMessage.EmptyHeaders, invocationId: "xyz", target: "method", argumentBindingException: null, 42),
                    encoded: Arr(Map(), HubProtocolConstants.StreamInvocationMessageType, "xyz", "method", Arr(42)),
                    binary: "lYAEo3h5eqZtZXRob2SRKg=="),
            },
            new object[] {
                new ProtocolTestData(
                    name: "StreamInvocationWithNoHeadersAndIntAndStringArgs",
                    message: new StreamInvocationMessage(HubMessage.EmptyHeaders, invocationId: "xyz", target: "method", argumentBindingException: null, 42, "string"),
                    encoded: Arr(Map(), HubProtocolConstants.StreamInvocationMessageType, "xyz", "method", Arr(42, "string")),
                    binary: "lYAEo3h5eqZtZXRob2SSKqZzdHJpbmc="),
            },
            new object[] {
                new ProtocolTestData(
                    name: "StreamInvocationWithNoHeadersAndIntStringAndCustomObjectArgs",
                    message: new StreamInvocationMessage(HubMessage.EmptyHeaders, invocationId: "xyz", target: "method", argumentBindingException: null, 42, "string", new CustomObject()),
                    encoded: Arr(Map(), HubProtocolConstants.StreamInvocationMessageType, "xyz", "method", Arr(42, "string", CustomObjectSerialized)),
                    binary: "lYAEo3h5eqZtZXRob2STKqZzdHJpbmeGq0J5dGVBcnJQcm9wxAMBAgOsRGF0ZVRpbWVQcm9w0wjUgG2ydsAAqkRvdWJsZVByb3DLQBkh+1RCzxKnSW50UHJvcCqoTnVsbFByb3DAqlN0cmluZ1Byb3CoU2lnbmFsUiE="),
            },
            new object[] {
                new ProtocolTestData(
                    name: "StreamInvocationWithNoHeadersAndCustomObjectArrayArg",
                    message: new StreamInvocationMessage(HubMessage.EmptyHeaders, invocationId: "xyz", target: "method", argumentBindingException: null, new[] { new CustomObject(), new CustomObject() }),
                    encoded: Arr(Map(), HubProtocolConstants.StreamInvocationMessageType, "xyz", "method", Arr(CustomObjectSerialized, CustomObjectSerialized)),
                    binary: "lYAEo3h5eqZtZXRob2SShqtCeXRlQXJyUHJvcMQDAQIDrERhdGVUaW1lUHJvcNMI1IBtsnbAAKpEb3VibGVQcm9wy0AZIftUQs8Sp0ludFByb3AqqE51bGxQcm9wwKpTdHJpbmdQcm9wqFNpZ25hbFIhhqtCeXRlQXJyUHJvcMQDAQIDrERhdGVUaW1lUHJvcNMI1IBtsnbAAKpEb3VibGVQcm9wy0AZIftUQs8Sp0ludFByb3AqqE51bGxQcm9wwKpTdHJpbmdQcm9wqFNpZ25hbFIh"),
            },
            new object[] {
                new ProtocolTestData(
                    name: "StreamInvocationWithHeadersAndCustomObjectArrayArg",
                    message: new StreamInvocationMessage(TestHeaders, invocationId: "xyz", target: "method", argumentBindingException: null, new[] { new CustomObject(), new CustomObject() }),
                    encoded: Arr(TestHeadersSerialized, HubProtocolConstants.StreamInvocationMessageType, "xyz", "method", Arr(CustomObjectSerialized, CustomObjectSerialized)),
                    binary: "lYOjRm9vo0JhcrJLZXlXaXRoCk5ldw0KTGluZXOrU3RpbGwgV29ya3OxVmFsdWVXaXRoTmV3TGluZXOwQWxzbwpXb3Jrcw0KRmluZQSjeHl6pm1ldGhvZJKGq0J5dGVBcnJQcm9wxAMBAgOsRGF0ZVRpbWVQcm9w0wjUgG2ydsAAqkRvdWJsZVByb3DLQBkh+1RCzxKnSW50UHJvcCqoTnVsbFByb3DAqlN0cmluZ1Byb3CoU2lnbmFsUiGGq0J5dGVBcnJQcm9wxAMBAgOsRGF0ZVRpbWVQcm9w0wjUgG2ydsAAqkRvdWJsZVByb3DLQBkh+1RCzxKnSW50UHJvcCqoTnVsbFByb3DAqlN0cmluZ1Byb3CoU2lnbmFsUiE="),
            },

            // CancelInvocation Messages
            new object[] {
                new ProtocolTestData(
                    name: "CancelInvocationWithNoHeaders",
                    message: new CancelInvocationMessage(HubMessage.EmptyHeaders, invocationId: "xyz"),
                    encoded: Arr(Map(), HubProtocolConstants.CancelInvocationMessageType, "xyz"),
                    binary: "k4AFo3h5eg=="),
            },
            new object[] {
                new ProtocolTestData(
                    name: "CancelInvocationWithHeaders",
                    message: new CancelInvocationMessage(TestHeaders, invocationId: "xyz"),
                    encoded: Arr(TestHeadersSerialized, HubProtocolConstants.CancelInvocationMessageType, "xyz"),
                    binary: "k4OjRm9vo0JhcrJLZXlXaXRoCk5ldw0KTGluZXOrU3RpbGwgV29ya3OxVmFsdWVXaXRoTmV3TGluZXOwQWxzbwpXb3Jrcw0KRmluZQWjeHl6"),
            },

            // Ping Messages
            new object[] {
                new ProtocolTestData(
                    name: "Ping",
                    message: PingMessage.Instance,
                    encoded: Arr(Map(), HubProtocolConstants.PingMessageType),
                    binary: "koAG"),
            },
        };

        [Theory]
        [MemberData(nameof(TestData))]
        public void ParseMessages(ProtocolTestData testData)
        {
            var bytes = Frame(testData.Encoded);
            var protocol = new MessagePackHubProtocol();
            Assert.True(protocol.TryParseMessages(bytes, new TestBinder(testData.Message), out var messages));

            Assert.Equal(1, messages.Count);
            Assert.Equal(testData.Message, messages[0], TestHubMessageEqualityComparer.Instance);

            // Unframe the message to check the binary encoding
            var byteSpan = bytes.AsReadOnlySpan();
            Assert.True(BinaryMessageParser.TryParseMessage(ref byteSpan, out var unframed));

            // Check the baseline binary encoding, use Assert.True in order to configure the error message
            var actual = Convert.ToBase64String(unframed.ToArray());
            Assert.True(string.Equals(actual, testData.Binary, StringComparison.Ordinal), $"Binary encoding changed from [{testData.Binary}] to [{actual}]. Please verify the MsgPack output and update the baseline");
        }

        [Theory]
        [MemberData(nameof(TestData))]
        public void WriteMessages(ProtocolTestData testData)
        {
            var bytes = Write(testData.Message);
            AssertMessages(testData.Encoded, bytes);

            // Unframe the message to check the binary encoding
            var byteSpan = bytes.AsReadOnlySpan();
            Assert.True(BinaryMessageParser.TryParseMessage(ref byteSpan, out var unframed));

            // Check the baseline binary encoding, use Assert.True in order to configure the error message
            var actual = Convert.ToBase64String(unframed.ToArray());
            Assert.True(string.Equals(actual, testData.Binary, StringComparison.Ordinal), $"Binary encoding changed from [{testData.Binary}] to [{actual}]. Please verify the MsgPack output and update the baseline");
        }

        public static IEnumerable<object[]> InvalidPayloads => new[]
        {
            // Headers
            new object[] { new InvalidMessageData("HeadersNotAMap", Arr("foo"), "Reading map length for 'headers' failed.") },
            new object[] { new InvalidMessageData("HeaderKeyInt", Arr(Map((42, "foo"))), "Reading 'headers[0].Key' as String failed.") },
            new object[] { new InvalidMessageData("HeaderValueInt", Arr(Map(("foo", 42))), "Reading 'headers[0].Value' as String failed.") },
            new object[] { new InvalidMessageData("HeaderKeyArray", Arr(Map(("biz", "boz"), (Arr(), "foo"))), "Reading 'headers[1].Key' as String failed.") },
            new object[] { new InvalidMessageData("HeaderValueArray", Arr(Map(("biz", "boz"), ("foo", Arr()))), "Reading 'headers[1].Value' as String failed.") },

            // Message Type
            new object[] { new InvalidMessageData("MessageTypeString", Arr(Map(), "foo"), "Reading 'messageType' as Int32 failed.") },
            new object[] { new InvalidMessageData("MessageTypeOutOfRange", Arr(Map(), 10), "Invalid message type: 10.") },

            // InvocationMessage
            new object[] { new InvalidMessageData("InvocationMissingId", Arr(Map(), HubProtocolConstants.InvocationMessageType), "Reading 'invocationId' as String failed.") },
            new object[] { new InvalidMessageData("InvocationIdBoolean", Arr(Map(), HubProtocolConstants.InvocationMessageType, false), "Reading 'invocationId' as String failed.") },
            new object[] { new InvalidMessageData("InvocationTargetMissing", Arr(Map(), HubProtocolConstants.InvocationMessageType, "abc"), "Reading 'target' as String failed.") },
            new object[] { new InvalidMessageData("InvocationTargetInt", Arr(Map(), HubProtocolConstants.InvocationMessageType, "abc", 42), "Reading 'target' as String failed.") },

            // StreamInvocationMessage
            new object[] { new InvalidMessageData("StreamInvocationMissingId", Arr(Map(), HubProtocolConstants.StreamInvocationMessageType), "Reading 'invocationId' as String failed.") },
            new object[] { new InvalidMessageData("StreamInvocationIdBoolean", Arr(Map(), HubProtocolConstants.StreamInvocationMessageType, false), "Reading 'invocationId' as String failed.") },
            new object[] { new InvalidMessageData("StreamInvocationTargetMissing", Arr(Map(), HubProtocolConstants.StreamInvocationMessageType, "abc"), "Reading 'target' as String failed.") },
            new object[] { new InvalidMessageData("StreamInvocationTargetInt", Arr(Map(), HubProtocolConstants.StreamInvocationMessageType, "abc", 42), "Reading 'target' as String failed.") },

            // StreamItemMessage
            new object[] { new InvalidMessageData("StreamItemMissingId", Arr(Map(), HubProtocolConstants.StreamItemMessageType), "Reading 'invocationId' as String failed.") },
            new object[] { new InvalidMessageData("StreamItemInvocationIdBoolean", Arr(Map(), HubProtocolConstants.StreamItemMessageType, false), "Reading 'invocationId' as String failed.") },
            new object[] { new InvalidMessageData("StreamItemMissing", Arr(Map(), HubProtocolConstants.StreamItemMessageType, "xyz"), "Deserializing object of the `String` type for 'item' failed.") },
            new object[] { new InvalidMessageData("StreamItemTypeMismatch", Arr(Map(), HubProtocolConstants.StreamItemMessageType, "xyz", 42), "Deserializing object of the `String` type for 'item' failed.") },

            // CompletionMessage
            new object[] { new InvalidMessageData("CompletionMissingId", Arr(Map(), HubProtocolConstants.CompletionMessageType), "Reading 'invocationId' as String failed.") },
            new object[] { new InvalidMessageData("CompletionIdBoolean", Arr(Map(), HubProtocolConstants.CompletionMessageType, false), "Reading 'invocationId' as String failed.") },
            new object[] { new InvalidMessageData("CompletionResultKindString", Arr(Map(), HubProtocolConstants.CompletionMessageType, "xyz", "abc"), "Reading 'resultKind' as Int32 failed.") },
            new object[] { new InvalidMessageData("CompletionResultKindOutOfRange", Arr(Map(), HubProtocolConstants.CompletionMessageType, "xyz", 42), "Invalid invocation result kind.") },
            new object[] { new InvalidMessageData("CompletionErrorMissing", Arr(Map(), HubProtocolConstants.CompletionMessageType, "xyz", 1), "Reading 'error' as String failed.") },
            new object[] { new InvalidMessageData("CompletionErrorInt", Arr(Map(), HubProtocolConstants.CompletionMessageType, "xyz", 1, 42), "Reading 'error' as String failed.") },
            new object[] { new InvalidMessageData("CompletionResultMissing", Arr(Map(), HubProtocolConstants.CompletionMessageType, "xyz", 3), "Deserializing object of the `String` type for 'argument' failed.") },
            new object[] { new InvalidMessageData("CompletionResultTypeMismatch", Arr(Map(), HubProtocolConstants.CompletionMessageType, "xyz", 3, 42), "Deserializing object of the `String` type for 'argument' failed.") },
        };

        [Theory]
        [MemberData(nameof(InvalidPayloads))]
        public void ParserThrowsForInvalidMessages(InvalidMessageData testData)
        {
            var buffer = Frame(new[] { testData.Encoded });
            var binder = new TestBinder(new[] { typeof(string) }, typeof(string));
            var exception = Assert.Throws<FormatException>(() => _hubProtocol.TryParseMessages(buffer, binder, out var messages));

            Assert.Equal(testData.ErrorMessage, exception.Message);
        }

        public static IEnumerable<object[]> ArgumentBindingErrors => new[]
        {
            // InvocationMessage
            new object[] {new InvalidMessageData("InvocationArgumentArrayMissing", Arr(Map(), HubProtocolConstants.InvocationMessageType, "abc", "xyz"), "Reading array length for 'arguments' failed.") },
            new object[] {new InvalidMessageData("InvocationArgumentArrayNotAnArray", Arr(Map(), HubProtocolConstants.InvocationMessageType, "abc", "xyz", 42), "Reading array length for 'arguments' failed.") },
            new object[] {new InvalidMessageData("InvocationArgumentArraySizeMismatchEmpty", Arr(Map(), HubProtocolConstants.InvocationMessageType, "abc", "xyz", Arr()), "Invocation provides 0 argument(s) but target expects 1.") },
            new object[] {new InvalidMessageData("InvocationArgumentArraySizeMismatchTooLarge", Arr(Map(), HubProtocolConstants.InvocationMessageType, "abc", "xyz", Arr("a", "b")), "Invocation provides 2 argument(s) but target expects 1.") },
            new object[] {new InvalidMessageData("InvocationArgumentTypeMismatch", Arr(Map(), HubProtocolConstants.InvocationMessageType, "abc", "xyz", Arr(42)), "Error binding arguments. Make sure that the types of the provided values match the types of the hub method being invoked.") },

            // StreamInvocationMessage
            new object[] {new InvalidMessageData("StreamInvocationArgumentArrayMissing", Arr(Map(), HubProtocolConstants.StreamInvocationMessageType, "abc", "xyz"), "Reading array length for 'arguments' failed.") }, // array is missing
            new object[] {new InvalidMessageData("StreamInvocationArgumentArrayNotAnArray", Arr(Map(), HubProtocolConstants.StreamInvocationMessageType, "abc", "xyz", 42), "Reading array length for 'arguments' failed.") }, // arguments isn't an array
            new object[] {new InvalidMessageData("StreamInvocationArgumentArraySizeMismatchEmpty", Arr(Map(), HubProtocolConstants.StreamInvocationMessageType, "abc", "xyz", Arr()), "Invocation provides 0 argument(s) but target expects 1.") }, // array is missing elements
            new object[] {new InvalidMessageData("StreamInvocationArgumentArraySizeMismatchTooLarge", Arr(Map(), HubProtocolConstants.StreamInvocationMessageType, "abc", "xyz", Arr("a", "b")), "Invocation provides 2 argument(s) but target expects 1.") }, // argument count does not match binder argument count
            new object[] {new InvalidMessageData("StreamInvocationArgumentTypeMismatch", Arr(Map(), HubProtocolConstants.StreamInvocationMessageType, "abc", "xyz", Arr(42)), "Error binding arguments. Make sure that the types of the provided values match the types of the hub method being invoked.") }, // argument type mismatch
        };

        [Theory]
        [MemberData(nameof(ArgumentBindingErrors))]
        public void GettingArgumentsThrowsIfBindingFailed(InvalidMessageData testData)
        {
            var buffer = Frame(new[] { testData.Encoded });
            var binder = new TestBinder(new[] { typeof(string) }, typeof(string));
            _hubProtocol.TryParseMessages(buffer, binder, out var messages);
            var exception = Assert.Throws<FormatException>(() => ((HubMethodInvocationMessage)messages[0]).Arguments);

            Assert.Equal(testData.ErrorMessage, exception.Message);
        }

        [Theory]
        [InlineData(new object[] { new byte[] { 0x05, 0x01 }, 0 })]
        public void ParserDoesNotConsumePartialData(byte[] payload, int expectedMessagesCount)
        {
            var binder = new TestBinder(new[] { typeof(string) }, typeof(string));
            var result = _hubProtocol.TryParseMessages(payload, binder, out var messages);
            Assert.True(result || messages.Count == 0);
            Assert.Equal(expectedMessagesCount, messages.Count);
        }

        [Fact]
        public void SerializerCanSerializeTypesWithNoDefaultCtor()
        {
            var result = Write(CompletionMessage.WithResult(HubMessage.EmptyHeaders, "0", new List<int> { 42 }.AsReadOnly()));
            AssertMessages(new[] { Arr(Map(), HubProtocolConstants.CompletionMessageType, "0", 3, Arr(42)) }, result);
        }

        private static void AssertMessages(MessagePackObject expectedOutput, ReadOnlySpan<byte> bytes)
        {
            Assert.True(BinaryMessageParser.TryParseMessage(ref bytes, out var message));
            var obj = Unpack(message.ToArray());
            Assert.Equal(expectedOutput, obj);
        }

        private static byte[] Frame(MessagePackObject input)
        {
            using (var stream = new MemoryStream())
            {
                BinaryMessageFormatter.WriteMessage(Pack(input), stream);
                stream.Flush();
                return stream.ToArray();
            }
        }

        private static MessagePackObject Unpack(byte[] input)
        {
            using (var stream = new MemoryStream(input))
            {
                using (var unpacker = Unpacker.Create(stream))
                {
                    Assert.True(unpacker.ReadObject(out var obj));
                    return obj;
                }
            }
        }

        private static byte[] Pack(MessagePackObject input)
        {
            var options = new PackingOptions()
            {
                StringEncoding = Encoding.UTF8
            };

            using (var stream = new MemoryStream())
            {
                using (var packer = Packer.Create(stream))
                {
                    input.PackToMessage(packer, options);
                    packer.Flush();
                }
                stream.Flush();
                return stream.ToArray();
            }
        }

        private static byte[] Write(HubMessage message)
        {
            var protocol = new MessagePackHubProtocol();
            using (var stream = new MemoryStream())
            {
                protocol.WriteMessage(message, stream);
                stream.Flush();
                return stream.ToArray();
            }
        }

        public class InvalidMessageData
        {
            public string Name { get; private set; }
            public MessagePackObject Encoded { get; private set; }
            public string ErrorMessage { get; private set; }

            public InvalidMessageData(string name, MessagePackObject encoded, string errorMessage)
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
            public MessagePackObject Encoded { get; }
            public HubMessage Message { get; }

            public ProtocolTestData(string name, HubMessage message, MessagePackObject encoded, string binary)
            {
                Name = name;
                Message = message;
                Encoded = encoded;
                Binary = binary;
            }

            public override string ToString() => Name;
        }
    }
}
