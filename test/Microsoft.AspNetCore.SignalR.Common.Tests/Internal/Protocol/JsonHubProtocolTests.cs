// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.AspNetCore.SignalR.Internal.Formatters;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Xunit;

namespace Microsoft.AspNetCore.SignalR.Common.Tests.Internal.Protocol
{
    public class JsonHubProtocolTests
    {
        private static readonly IReadOnlyDictionary<string, string> TestHeaders = new Dictionary<string, string>
        {
            { "Foo", "Bar" },
            { "KeyWith\nNew\r\nLines", "Still Works" },
            { "ValueWithNewLines", "Also\nWorks\r\nFine" },
        };

        // It's cleaner to do this as a prefix and use concatenation rather than string interpolation because JSON is already filled with '{'s.
        private static readonly string WithHeadersPrefix = "{\"headers\":{\"Foo\":\"Bar\",\"KeyWith\\nNew\\r\\nLines\":\"Still Works\",\"ValueWithNewLines\":\"Also\\nWorks\\r\\nFine\"}";

        public static IEnumerable<object[]> ProtocolTestData => new[]
        {
            new object[] { new InvocationMessage(HubMessage.EmptyHeaders, "123", "Target", null, 1, "Foo", 2.0f), true, NullValueHandling.Ignore, "{\"type\":1,\"invocationId\":\"123\",\"target\":\"Target\",\"arguments\":[1,\"Foo\",2.0]}" },
            new object[] { new InvocationMessage(HubMessage.EmptyHeaders, null, "Target", null, 1, "Foo", 2.0f), true, NullValueHandling.Ignore, "{\"type\":1,\"target\":\"Target\",\"arguments\":[1,\"Foo\",2.0]}" },
            new object[] { new InvocationMessage(HubMessage.EmptyHeaders, null, "Target", null, true), true, NullValueHandling.Ignore, "{\"type\":1,\"target\":\"Target\",\"arguments\":[true]}" },
            new object[] { new InvocationMessage(HubMessage.EmptyHeaders, null, "Target", null, new object[] { null }), true, NullValueHandling.Ignore, "{\"type\":1,\"target\":\"Target\",\"arguments\":[null]}" },
            new object[] { new InvocationMessage(HubMessage.EmptyHeaders, null, "Target", null, new CustomObject()), false, NullValueHandling.Ignore, "{\"type\":1,\"target\":\"Target\",\"arguments\":[{\"StringProp\":\"SignalR!\",\"DoubleProp\":6.2831853071,\"IntProp\":42,\"DateTimeProp\":\"2017-04-11T00:00:00\",\"ByteArrProp\":\"AQID\"}]}" },
            new object[] { new InvocationMessage(HubMessage.EmptyHeaders, null, "Target", null, new CustomObject()), true, NullValueHandling.Ignore, "{\"type\":1,\"target\":\"Target\",\"arguments\":[{\"stringProp\":\"SignalR!\",\"doubleProp\":6.2831853071,\"intProp\":42,\"dateTimeProp\":\"2017-04-11T00:00:00\",\"byteArrProp\":\"AQID\"}]}" },
            new object[] { new InvocationMessage(HubMessage.EmptyHeaders, null, "Target", null, new CustomObject()), false, NullValueHandling.Include, "{\"type\":1,\"target\":\"Target\",\"arguments\":[{\"StringProp\":\"SignalR!\",\"DoubleProp\":6.2831853071,\"IntProp\":42,\"DateTimeProp\":\"2017-04-11T00:00:00\",\"NullProp\":null,\"ByteArrProp\":\"AQID\"}]}" },
            new object[] { new InvocationMessage(HubMessage.EmptyHeaders, null, "Target", null, new CustomObject()), true, NullValueHandling.Include, "{\"type\":1,\"target\":\"Target\",\"arguments\":[{\"stringProp\":\"SignalR!\",\"doubleProp\":6.2831853071,\"intProp\":42,\"dateTimeProp\":\"2017-04-11T00:00:00\",\"nullProp\":null,\"byteArrProp\":\"AQID\"}]}" },
            new object[] { new InvocationMessage(TestHeaders, "123", "Target", null, 1, "Foo", 2.0f), true, NullValueHandling.Ignore, WithHeadersPrefix + ",\"type\":1,\"invocationId\":\"123\",\"target\":\"Target\",\"arguments\":[1,\"Foo\",2.0]}" },

            new object[] { new StreamItemMessage(HubMessage.EmptyHeaders, "123", 1), true, NullValueHandling.Ignore, "{\"type\":2,\"invocationId\":\"123\",\"item\":1}" },
            new object[] { new StreamItemMessage(HubMessage.EmptyHeaders, "123", "Foo"), true, NullValueHandling.Ignore, "{\"type\":2,\"invocationId\":\"123\",\"item\":\"Foo\"}" },
            new object[] { new StreamItemMessage(HubMessage.EmptyHeaders, "123", 2.0f), true, NullValueHandling.Ignore, "{\"type\":2,\"invocationId\":\"123\",\"item\":2.0}" },
            new object[] { new StreamItemMessage(HubMessage.EmptyHeaders, "123", true), true, NullValueHandling.Ignore, "{\"type\":2,\"invocationId\":\"123\",\"item\":true}" },
            new object[] { new StreamItemMessage(HubMessage.EmptyHeaders, "123", null), true, NullValueHandling.Ignore, "{\"type\":2,\"invocationId\":\"123\",\"item\":null}" },
            new object[] { new StreamItemMessage(HubMessage.EmptyHeaders, "123", new CustomObject()), false, NullValueHandling.Ignore, "{\"type\":2,\"invocationId\":\"123\",\"item\":{\"StringProp\":\"SignalR!\",\"DoubleProp\":6.2831853071,\"IntProp\":42,\"DateTimeProp\":\"2017-04-11T00:00:00\",\"ByteArrProp\":\"AQID\"}}" },
            new object[] { new StreamItemMessage(HubMessage.EmptyHeaders, "123", new CustomObject()), true, NullValueHandling.Ignore, "{\"type\":2,\"invocationId\":\"123\",\"item\":{\"stringProp\":\"SignalR!\",\"doubleProp\":6.2831853071,\"intProp\":42,\"dateTimeProp\":\"2017-04-11T00:00:00\",\"byteArrProp\":\"AQID\"}}" },
            new object[] { new StreamItemMessage(HubMessage.EmptyHeaders, "123", new CustomObject()), false, NullValueHandling.Include, "{\"type\":2,\"invocationId\":\"123\",\"item\":{\"StringProp\":\"SignalR!\",\"DoubleProp\":6.2831853071,\"IntProp\":42,\"DateTimeProp\":\"2017-04-11T00:00:00\",\"NullProp\":null,\"ByteArrProp\":\"AQID\"}}" },
            new object[] { new StreamItemMessage(HubMessage.EmptyHeaders, "123", new CustomObject()), true, NullValueHandling.Include, "{\"type\":2,\"invocationId\":\"123\",\"item\":{\"stringProp\":\"SignalR!\",\"doubleProp\":6.2831853071,\"intProp\":42,\"dateTimeProp\":\"2017-04-11T00:00:00\",\"nullProp\":null,\"byteArrProp\":\"AQID\"}}" },
            new object[] { new StreamItemMessage(TestHeaders, "123", new CustomObject()), true, NullValueHandling.Include, WithHeadersPrefix + ",\"type\":2,\"invocationId\":\"123\",\"item\":{\"stringProp\":\"SignalR!\",\"doubleProp\":6.2831853071,\"intProp\":42,\"dateTimeProp\":\"2017-04-11T00:00:00\",\"nullProp\":null,\"byteArrProp\":\"AQID\"}}" },

            new object[] { CompletionMessage.WithResult(HubMessage.EmptyHeaders, "123", 1), true, NullValueHandling.Ignore, "{\"type\":3,\"invocationId\":\"123\",\"result\":1}" },
            new object[] { CompletionMessage.WithResult(HubMessage.EmptyHeaders, "123", "Foo"), true, NullValueHandling.Ignore, "{\"type\":3,\"invocationId\":\"123\",\"result\":\"Foo\"}" },
            new object[] { CompletionMessage.WithResult(HubMessage.EmptyHeaders, "123", 2.0f), true, NullValueHandling.Ignore, "{\"type\":3,\"invocationId\":\"123\",\"result\":2.0}" },
            new object[] { CompletionMessage.WithResult(HubMessage.EmptyHeaders, "123", true), true, NullValueHandling.Ignore, "{\"type\":3,\"invocationId\":\"123\",\"result\":true}" },
            new object[] { CompletionMessage.WithResult(HubMessage.EmptyHeaders, "123", null), true, NullValueHandling.Ignore, "{\"type\":3,\"invocationId\":\"123\",\"result\":null}" },
            new object[] { CompletionMessage.WithResult(HubMessage.EmptyHeaders, "123", new CustomObject()), false, NullValueHandling.Ignore, "{\"type\":3,\"invocationId\":\"123\",\"result\":{\"StringProp\":\"SignalR!\",\"DoubleProp\":6.2831853071,\"IntProp\":42,\"DateTimeProp\":\"2017-04-11T00:00:00\",\"ByteArrProp\":\"AQID\"}}" },
            new object[] { CompletionMessage.WithResult(HubMessage.EmptyHeaders, "123", new CustomObject()), true, NullValueHandling.Ignore, "{\"type\":3,\"invocationId\":\"123\",\"result\":{\"stringProp\":\"SignalR!\",\"doubleProp\":6.2831853071,\"intProp\":42,\"dateTimeProp\":\"2017-04-11T00:00:00\",\"byteArrProp\":\"AQID\"}}" },
            new object[] { CompletionMessage.WithResult(HubMessage.EmptyHeaders, "123", new CustomObject()), false, NullValueHandling.Include, "{\"type\":3,\"invocationId\":\"123\",\"result\":{\"StringProp\":\"SignalR!\",\"DoubleProp\":6.2831853071,\"IntProp\":42,\"DateTimeProp\":\"2017-04-11T00:00:00\",\"NullProp\":null,\"ByteArrProp\":\"AQID\"}}" },
            new object[] { CompletionMessage.WithResult(HubMessage.EmptyHeaders, "123", new CustomObject()), true, NullValueHandling.Include, "{\"type\":3,\"invocationId\":\"123\",\"result\":{\"stringProp\":\"SignalR!\",\"doubleProp\":6.2831853071,\"intProp\":42,\"dateTimeProp\":\"2017-04-11T00:00:00\",\"nullProp\":null,\"byteArrProp\":\"AQID\"}}" },
            new object[] { CompletionMessage.WithResult(TestHeaders, "123", new CustomObject()), true, NullValueHandling.Include, WithHeadersPrefix + ",\"type\":3,\"invocationId\":\"123\",\"result\":{\"stringProp\":\"SignalR!\",\"doubleProp\":6.2831853071,\"intProp\":42,\"dateTimeProp\":\"2017-04-11T00:00:00\",\"nullProp\":null,\"byteArrProp\":\"AQID\"}}" },
            new object[] { CompletionMessage.WithError(HubMessage.EmptyHeaders, "123", "Whoops!"), false, NullValueHandling.Ignore, "{\"type\":3,\"invocationId\":\"123\",\"error\":\"Whoops!\"}" },
            new object[] { CompletionMessage.WithError(TestHeaders, "123", "Whoops!"), false, NullValueHandling.Ignore, WithHeadersPrefix + ",\"type\":3,\"invocationId\":\"123\",\"error\":\"Whoops!\"}" },
            new object[] { CompletionMessage.Empty(HubMessage.EmptyHeaders, "123"), true, NullValueHandling.Ignore, "{\"type\":3,\"invocationId\":\"123\"}" },
            new object[] { CompletionMessage.Empty(TestHeaders, "123"), true, NullValueHandling.Ignore, WithHeadersPrefix + ",\"type\":3,\"invocationId\":\"123\"}" },

            new object[] { new StreamInvocationMessage(HubMessage.EmptyHeaders, "123", "Target", null, 1, "Foo", 2.0f), true, NullValueHandling.Ignore, "{\"type\":4,\"invocationId\":\"123\",\"target\":\"Target\",\"arguments\":[1,\"Foo\",2.0]}" },
            new object[] { new StreamInvocationMessage(HubMessage.EmptyHeaders, "123", "Target", null, 1, "Foo", 2.0f), true, NullValueHandling.Ignore, "{\"type\":4,\"invocationId\":\"123\",\"target\":\"Target\",\"arguments\":[1,\"Foo\",2.0]}" },
            new object[] { new StreamInvocationMessage(HubMessage.EmptyHeaders, "123", "Target", null, true), true, NullValueHandling.Ignore, "{\"type\":4,\"invocationId\":\"123\",\"target\":\"Target\",\"arguments\":[true]}" },
            new object[] { new StreamInvocationMessage(HubMessage.EmptyHeaders, "123", "Target", null, new object[] { null }), true, NullValueHandling.Ignore, "{\"type\":4,\"invocationId\":\"123\",\"target\":\"Target\",\"arguments\":[null]}" },
            new object[] { new StreamInvocationMessage(HubMessage.EmptyHeaders, "123", "Target", null, new CustomObject()), false, NullValueHandling.Ignore, "{\"type\":4,\"invocationId\":\"123\",\"target\":\"Target\",\"arguments\":[{\"StringProp\":\"SignalR!\",\"DoubleProp\":6.2831853071,\"IntProp\":42,\"DateTimeProp\":\"2017-04-11T00:00:00\",\"ByteArrProp\":\"AQID\"}]}" },
            new object[] { new StreamInvocationMessage(HubMessage.EmptyHeaders, "123", "Target", null, new CustomObject()), true, NullValueHandling.Ignore, "{\"type\":4,\"invocationId\":\"123\",\"target\":\"Target\",\"arguments\":[{\"stringProp\":\"SignalR!\",\"doubleProp\":6.2831853071,\"intProp\":42,\"dateTimeProp\":\"2017-04-11T00:00:00\",\"byteArrProp\":\"AQID\"}]}" },
            new object[] { new StreamInvocationMessage(HubMessage.EmptyHeaders, "123", "Target", null, new CustomObject()), false, NullValueHandling.Include, "{\"type\":4,\"invocationId\":\"123\",\"target\":\"Target\",\"arguments\":[{\"StringProp\":\"SignalR!\",\"DoubleProp\":6.2831853071,\"IntProp\":42,\"DateTimeProp\":\"2017-04-11T00:00:00\",\"NullProp\":null,\"ByteArrProp\":\"AQID\"}]}" },
            new object[] { new StreamInvocationMessage(HubMessage.EmptyHeaders, "123", "Target", null, new CustomObject()), true, NullValueHandling.Include, "{\"type\":4,\"invocationId\":\"123\",\"target\":\"Target\",\"arguments\":[{\"stringProp\":\"SignalR!\",\"doubleProp\":6.2831853071,\"intProp\":42,\"dateTimeProp\":\"2017-04-11T00:00:00\",\"nullProp\":null,\"byteArrProp\":\"AQID\"}]}" },
            new object[] { new StreamInvocationMessage(TestHeaders, "123", "Target", null, new CustomObject()), true, NullValueHandling.Include, WithHeadersPrefix + ",\"type\":4,\"invocationId\":\"123\",\"target\":\"Target\",\"arguments\":[{\"stringProp\":\"SignalR!\",\"doubleProp\":6.2831853071,\"intProp\":42,\"dateTimeProp\":\"2017-04-11T00:00:00\",\"nullProp\":null,\"byteArrProp\":\"AQID\"}]}" },

            new object[] { new CancelInvocationMessage(HubMessage.EmptyHeaders, "123"), true, NullValueHandling.Ignore, "{\"type\":5,\"invocationId\":\"123\"}" },
            new object[] { new CancelInvocationMessage(TestHeaders, "123"), true, NullValueHandling.Ignore, WithHeadersPrefix + ",\"type\":5,\"invocationId\":\"123\"}" },

            new object[] { PingMessage.Instance, true, NullValueHandling.Ignore, "{\"type\":6}" },
        };

        [Theory]
        [MemberData(nameof(ProtocolTestData))]
        public void WriteMessage(HubMessage message, bool camelCase, NullValueHandling nullValueHandling, string expectedOutput)
        {
            expectedOutput = Frame(expectedOutput);

            var protocolOptions = new JsonHubProtocolOptions
            {
                PayloadSerializerSettings = new JsonSerializerSettings()
                {
                    NullValueHandling = nullValueHandling,
                    ContractResolver = camelCase ? new CamelCasePropertyNamesContractResolver() : new DefaultContractResolver()
                }
            };

            var protocol = new JsonHubProtocol(Options.Create(protocolOptions));

            using (var ms = new MemoryStream())
            {
                protocol.WriteMessage(message, ms);
                var json = Encoding.UTF8.GetString(ms.ToArray());

                Assert.Equal(expectedOutput, json);
            }
        }

        [Theory]
        [MemberData(nameof(ProtocolTestData))]
        public void ParseMessage(HubMessage expectedMessage, bool camelCase, NullValueHandling nullValueHandling, string input)
        {
            input = Frame(input);

            var protocolOptions = new JsonHubProtocolOptions
            {
                PayloadSerializerSettings = new JsonSerializerSettings
                {
                    NullValueHandling = nullValueHandling,
                    ContractResolver = camelCase ? new CamelCasePropertyNamesContractResolver() : new DefaultContractResolver()
                }
            };

            var binder = new TestBinder(expectedMessage);
            var protocol = new JsonHubProtocol(Options.Create(protocolOptions));
            protocol.TryParseMessages(Encoding.UTF8.GetBytes(input), binder, out var messages);

            Assert.Equal(expectedMessage, messages[0], TestHubMessageEqualityComparer.Instance);
        }

        [Theory]
        [InlineData("", "Error reading JSON.")]
        [InlineData("null", "Unexpected JSON Token Type 'Null'. Expected a JSON Object.")]
        [InlineData("42", "Unexpected JSON Token Type 'Integer'. Expected a JSON Object.")]
        [InlineData("'foo'", "Unexpected JSON Token Type 'String'. Expected a JSON Object.")]
        [InlineData("[42]", "Unexpected JSON Token Type 'Array'. Expected a JSON Object.")]
        [InlineData("{}", "Missing required property 'type'.")]

        [InlineData("{'type':1,'headers':{\"Foo\": 42}}", "Expected header 'Foo' to be of type String.")]
        [InlineData("{'type':1,'headers':{\"Foo\": true}}", "Expected header 'Foo' to be of type String.")]
        [InlineData("{'type':1,'headers':{\"Foo\": null}}", "Expected header 'Foo' to be of type String.")]
        [InlineData("{'type':1,'headers':{\"Foo\": []}}", "Expected header 'Foo' to be of type String.")]

        [InlineData("{'type':1}", "Missing required property 'target'.")]
        [InlineData("{'type':1,'invocationId':42}", "Expected 'invocationId' to be of type String.")]
        [InlineData("{'type':1,'invocationId':'42'}", "Missing required property 'target'.")]
        [InlineData("{'type':1,'invocationId':'42','target':42}", "Expected 'target' to be of type String.")]
        [InlineData("{'type':1,'invocationId':'42','target':'foo'}", "Missing required property 'arguments'.")]
        [InlineData("{'type':1,'invocationId':'42','target':'foo','arguments':{}}", "Expected 'arguments' to be of type Array.")]

        [InlineData("{'type':2}", "Missing required property 'invocationId'.")]
        [InlineData("{'type':2,'invocationId':42}", "Expected 'invocationId' to be of type String.")]
        [InlineData("{'type':2,'invocationId':'42'}", "Missing required property 'item'.")]

        [InlineData("{'type':3}", "Missing required property 'invocationId'.")]
        [InlineData("{'type':3,'invocationId':42}", "Expected 'invocationId' to be of type String.")]
        [InlineData("{'type':3,'invocationId':'42','error':[]}", "Expected 'error' to be of type String.")]

        [InlineData("{'type':4}", "Missing required property 'invocationId'.")]
        [InlineData("{'type':4,'invocationId':42}", "Expected 'invocationId' to be of type String.")]
        [InlineData("{'type':4,'invocationId':'42','target':42}", "Expected 'target' to be of type String.")]
        [InlineData("{'type':4,'invocationId':'42','target':'foo'}", "Missing required property 'arguments'.")]
        [InlineData("{'type':4,'invocationId':'42','target':'foo','arguments':{}}", "Expected 'arguments' to be of type Array.")]

        [InlineData("{'type':9}", "Unknown message type: 9")]
        [InlineData("{'type':'foo'}", "Expected 'type' to be of type Integer.")]

        [InlineData("{'type':3,'invocationId':'42','error':'foo','result':true}", "The 'error' and 'result' properties are mutually exclusive.")]
        public void InvalidMessages(string input, string expectedMessage)
        {
            input = Frame(input);

            var binder = new TestBinder();
            var protocol = new JsonHubProtocol();
            var ex = Assert.Throws<InvalidDataException>(() => protocol.TryParseMessages(Encoding.UTF8.GetBytes(input), binder, out var messages));
            Assert.Equal(expectedMessage, ex.Message);
        }

        [Theory]
        [InlineData("{'type':1,'invocationId':'42','target':'foo','arguments':[]}", "Invocation provides 0 argument(s) but target expects 2.")]
        [InlineData("{'type':1,'invocationId':'42','target':'foo','arguments':[ 'abc', 'xyz']}", "Error binding arguments. Make sure that the types of the provided values match the types of the hub method being invoked.")]
        [InlineData("{'type':4,'invocationId':'42','target':'foo','arguments':[]}", "Invocation provides 0 argument(s) but target expects 2.")]
        [InlineData("{'type':4,'invocationId':'42','target':'foo','arguments':[ 'abc', 'xyz']}", "Error binding arguments. Make sure that the types of the provided values match the types of the hub method being invoked.")]
        public void ArgumentBindingErrors(string input, string expectedMessage)
        {
            input = Frame(input);

            var binder = new TestBinder(paramTypes: new[] { typeof(int), typeof(string) }, returnType: typeof(bool));
            var protocol = new JsonHubProtocol();
            protocol.TryParseMessages(Encoding.UTF8.GetBytes(input), binder, out var messages);
            var ex = Assert.Throws<InvalidDataException>(() => ((HubMethodInvocationMessage)messages[0]).Arguments);
            Assert.Equal(expectedMessage, ex.Message);
        }

        private static string Frame(string input)
        {
            var data = Encoding.UTF8.GetBytes(input);
            return Encoding.UTF8.GetString(FormatMessageToArray(data));
        }

        private static byte[] FormatMessageToArray(byte[] message)
        {
            var output = new MemoryStream();
            TextMessageFormatter.WriteMessage(message, output);
            return output.ToArray();
        }
    }
}
