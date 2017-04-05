using System;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Xunit;

namespace Microsoft.AspNetCore.SignalR.Common.Tests.Internal.Protocol
{
    public class JsonHubProtocolTests
    {
        [Theory]
        [InlineData("123", true, "Target", new object[] { 1, "Foo", 2.0f }, "{\"invocationId\":\"123\",\"type\":1,\"target\":\"Target\",\"nonBlocking\":true,\"arguments\":[1,\"Foo\",2.0]}")]
        [InlineData("123", false, "Target", new object[] { 1, "Foo", 2.0f }, "{\"invocationId\":\"123\",\"type\":1,\"target\":\"Target\",\"arguments\":[1,\"Foo\",2.0]}")]
        [InlineData("123", false, "Target", new object[] { true }, "{\"invocationId\":\"123\",\"type\":1,\"target\":\"Target\",\"arguments\":[true]}")]
        [InlineData("123", false, "Target", new object[] { null }, "{\"invocationId\":\"123\",\"type\":1,\"target\":\"Target\",\"arguments\":[null]}")]
        public async Task CanSerializeInvocationMessages(string invocationId, bool nonBlocking, string target, object[] arguments, string expectedOutput)
        {
            var message = new InvocationMessage(invocationId, target, arguments, nonBlocking);
            await TestMessageOutput(expectedOutput, message);
        }

        [Theory]
        [InlineData(false, NullValueHandling.Ignore, "{\"invocationId\":\"123\",\"type\":1,\"target\":\"Target\",\"arguments\":[{\"StringProp\":\"SignalR!\",\"DoubleProp\":6.2831853071,\"IntProp\":42,\"DateTimeProp\":\"2017-04-11T00:00:00\"}]}")]
        [InlineData(true, NullValueHandling.Ignore, "{\"invocationId\":\"123\",\"type\":1,\"target\":\"Target\",\"arguments\":[{\"stringProp\":\"SignalR!\",\"doubleProp\":6.2831853071,\"intProp\":42,\"dateTimeProp\":\"2017-04-11T00:00:00\"}]}")]
        [InlineData(false, NullValueHandling.Include, "{\"invocationId\":\"123\",\"type\":1,\"target\":\"Target\",\"arguments\":[{\"StringProp\":\"SignalR!\",\"DoubleProp\":6.2831853071,\"IntProp\":42,\"DateTimeProp\":\"2017-04-11T00:00:00\",\"NullProp\":null}]}")]
        [InlineData(true, NullValueHandling.Include, "{\"invocationId\":\"123\",\"type\":1,\"target\":\"Target\",\"arguments\":[{\"stringProp\":\"SignalR!\",\"doubleProp\":6.2831853071,\"intProp\":42,\"dateTimeProp\":\"2017-04-11T00:00:00\",\"nullProp\":null}]}")]
        public async Task CanSerializeCustomObjectInInvocation(bool camelCase, NullValueHandling nullHandling, string expectedOutput)
        {
            var message = new InvocationMessage("123", "Target", new[] { new CustomObject() }, nonBlocking: false);

            var payloadSerializer = new JsonSerializer();
            if(camelCase)
            {
                payloadSerializer.ContractResolver = new CamelCasePropertyNamesContractResolver();
            }
            payloadSerializer.NullValueHandling = nullHandling;

            await TestMessageOutput(expectedOutput, message, payloadSerializer);
        }

        [Theory]
        [InlineData("123", 1, "{\"invocationId\":\"123\",\"type\":2,\"result\":1}")]
        [InlineData("123", "Foo", "{\"invocationId\":\"123\",\"type\":2,\"result\":\"Foo\"}")]
        [InlineData("123", 2.0f, "{\"invocationId\":\"123\",\"type\":2,\"result\":2.0}")]
        [InlineData("123", true, "{\"invocationId\":\"123\",\"type\":2,\"result\":true}")]
        [InlineData("123", null, "{\"invocationId\":\"123\",\"type\":2,\"result\":null}")]
        public async Task CanSerializeResultMessages(string invocationId, object result, string expectedOutput)
        {
            var message = new StreamItemMessage(invocationId, result);

            var protocol = new JsonHubProtocol(new JsonSerializer());
            var encoded = await protocol.WriteToArrayAsync(message);
            var json = Encoding.UTF8.GetString(encoded);

            Assert.Equal(expectedOutput, json);
        }

        [Theory]
        [InlineData(false, NullValueHandling.Ignore, "{\"invocationId\":\"123\",\"type\":2,\"result\":{\"StringProp\":\"SignalR!\",\"DoubleProp\":6.2831853071,\"IntProp\":42,\"DateTimeProp\":\"2017-04-11T00:00:00\"}}")]
        [InlineData(true, NullValueHandling.Ignore, "{\"invocationId\":\"123\",\"type\":2,\"result\":{\"stringProp\":\"SignalR!\",\"doubleProp\":6.2831853071,\"intProp\":42,\"dateTimeProp\":\"2017-04-11T00:00:00\"}}")]
        [InlineData(false, NullValueHandling.Include, "{\"invocationId\":\"123\",\"type\":2,\"result\":{\"StringProp\":\"SignalR!\",\"DoubleProp\":6.2831853071,\"IntProp\":42,\"DateTimeProp\":\"2017-04-11T00:00:00\",\"NullProp\":null}}")]
        [InlineData(true, NullValueHandling.Include, "{\"invocationId\":\"123\",\"type\":2,\"result\":{\"stringProp\":\"SignalR!\",\"doubleProp\":6.2831853071,\"intProp\":42,\"dateTimeProp\":\"2017-04-11T00:00:00\",\"nullProp\":null}}")]
        public async Task CanSerializeCustomObjectInResult(bool camelCase, NullValueHandling nullHandling, string expectedOutput)
        {
            var message = new StreamItemMessage("123", new CustomObject());

            var jsonSerializer = new JsonSerializer();
            if(camelCase)
            {
                jsonSerializer.ContractResolver = new CamelCasePropertyNamesContractResolver();
            }
            jsonSerializer.NullValueHandling = nullHandling;

            var protocol = new JsonHubProtocol(jsonSerializer);
            var encoded = await protocol.WriteToArrayAsync(message);
            var json = Encoding.UTF8.GetString(encoded);

            Assert.Equal(expectedOutput, json);
        }

        [Theory]
        [InlineData("123", null, 1, true, "{\"invocationId\":\"123\",\"type\":3,\"result\":1}")]
        [InlineData("123", null, "Foo", true, "{\"invocationId\":\"123\",\"type\":3,\"result\":\"Foo\"}")]
        [InlineData("123", null, 2.0f, true, "{\"invocationId\":\"123\",\"type\":3,\"result\":2.0}")]
        [InlineData("123", null, true, true, "{\"invocationId\":\"123\",\"type\":3,\"result\":true}")]
        [InlineData("123", null, null, true, "{\"invocationId\":\"123\",\"type\":3,\"result\":null}")]
        [InlineData("123", "Whoops!", null, false, "{\"invocationId\":\"123\",\"type\":3,\"error\":\"Whoops!\"}")]
        public async Task CanSerializeCompletionMessages(string invocationId, string error, object result, bool hasResult, string expectedOutput)
        {
            var message = new CompletionMessage(invocationId, error, result, hasResult);

            var protocol = new JsonHubProtocol(new JsonSerializer());
            var encoded = await protocol.WriteToArrayAsync(message);
            var json = Encoding.UTF8.GetString(encoded);

            Assert.Equal(expectedOutput, json);
        }

        [Theory]
        [InlineData(false, NullValueHandling.Ignore, "{\"invocationId\":\"123\",\"type\":3,\"result\":{\"StringProp\":\"SignalR!\",\"DoubleProp\":6.2831853071,\"IntProp\":42,\"DateTimeProp\":\"2017-04-11T00:00:00\"}}")]
        [InlineData(true, NullValueHandling.Ignore, "{\"invocationId\":\"123\",\"type\":3,\"result\":{\"stringProp\":\"SignalR!\",\"doubleProp\":6.2831853071,\"intProp\":42,\"dateTimeProp\":\"2017-04-11T00:00:00\"}}")]
        [InlineData(false, NullValueHandling.Include, "{\"invocationId\":\"123\",\"type\":3,\"result\":{\"StringProp\":\"SignalR!\",\"DoubleProp\":6.2831853071,\"IntProp\":42,\"DateTimeProp\":\"2017-04-11T00:00:00\",\"NullProp\":null}}")]
        [InlineData(true, NullValueHandling.Include, "{\"invocationId\":\"123\",\"type\":3,\"result\":{\"stringProp\":\"SignalR!\",\"doubleProp\":6.2831853071,\"intProp\":42,\"dateTimeProp\":\"2017-04-11T00:00:00\",\"nullProp\":null}}")]
        public async Task CanSerializeCustomObjectInCompletion(bool camelCase, NullValueHandling nullHandling, string expectedOutput)
        {
            var message = new CompletionMessage("123", error: null, result: new CustomObject(), hasResult: true);

            var jsonSerializer = new JsonSerializer();
            if(camelCase)
            {
                jsonSerializer.ContractResolver = new CamelCasePropertyNamesContractResolver();
            }
            jsonSerializer.NullValueHandling = nullHandling;

            var protocol = new JsonHubProtocol(jsonSerializer);
            var encoded = await protocol.WriteToArrayAsync(message);
            var json = Encoding.UTF8.GetString(encoded);

            Assert.Equal(expectedOutput, json);
        }

        private static async Task TestMessageOutput(string expectedOutput, InvocationMessage message, JsonSerializer payloadSerializer = null)
        {
            var protocol = new JsonHubProtocol(payloadSerializer ?? new JsonSerializer());
            var encoded = await protocol.WriteToArrayAsync(message);
            var json = Encoding.UTF8.GetString(encoded);

            Assert.Equal(expectedOutput, json);
        }

        private class CustomObject
        {
            // Not intended to be a full set of things, just a smattering of sample serializations
            public string StringProp => "SignalR!";

            public double DoubleProp => 6.2831853071;

            public int IntProp => 42;

            public DateTime DateTimeProp => new DateTime(2017, 4, 11);

            public object NullProp => null;
        }
    }
}
