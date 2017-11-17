using System;
using System.Linq;
using System.Text;
using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.SignalR.Internal;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Microsoft.AspNetCore.SignalR.Microbenchmarks
{
    [Config(typeof(CoreConfig))]
    public class JsonHubProtocolBenchmark
    {
        private static readonly JsonHubProtocol HubProtocol = new JsonHubProtocol(new JsonSerializer
        {
            NullValueHandling = NullValueHandling.Ignore,
            ContractResolver = new CamelCasePropertyNamesContractResolver()
        });

        private byte[] _binaryInput;
        private TestBinder _binder;

        [Params(0, 1)]
        public int Input { get; set; }

        [IterationSetup]
        public void Setup()
        {
            switch (Input)
            {
                case 0:
                    _binaryInput = Encoding.UTF8.GetBytes("{\"invocationId\":\"123\",\"type\":1,\"target\":\"Target\",\"nonBlocking\":true,\"arguments\":[1,\"Foo\",2.0]}" + Encoding.UTF8.GetString(new[] { (byte)0x1e }));
                    _binder = new TestBinder(new InvocationMessage("123", true, "Target", null, 1, "Foo", 2.0f));
                    break;
                case 1:
                    _binaryInput = Encoding.UTF8.GetBytes("{\"invocationId\":\"123\",\"type\":5}" + Encoding.UTF8.GetString(new[] { (byte)0x1e }));
                    _binder = new TestBinder(new CancelInvocationMessage("123"));
                    break;
            }
        }

        [Benchmark]
        public void TryParseMessage()
        {
            if (!HubProtocol.TryParseMessages(_binaryInput, _binder, out var _))
            {
                throw new InvalidOperationException("Failed to parse");
            }
        }
    }

    internal class TestBinder : IInvocationBinder
    {
        private readonly Type[] _paramTypes;
        private readonly Type _returnType;

        public TestBinder(HubMessage expectedMessage)
        {
            switch (expectedMessage)
            {
                case StreamInvocationMessage i:
                    _paramTypes = i.Arguments?.Select(a => a?.GetType() ?? typeof(object))?.ToArray();
                    break;
                case InvocationMessage i:
                    _paramTypes = i.Arguments?.Select(a => a?.GetType() ?? typeof(object))?.ToArray();
                    break;
                case StreamItemMessage s:
                    _returnType = s.Item?.GetType() ?? typeof(object);
                    break;
                case CompletionMessage c:
                    _returnType = c.Result?.GetType() ?? typeof(object);
                    break;
            }
        }

        public TestBinder() : this(null, null) { }
        public TestBinder(Type[] paramTypes) : this(paramTypes, null) { }
        public TestBinder(Type returnType) : this(null, returnType) { }
        public TestBinder(Type[] paramTypes, Type returnType)
        {
            _paramTypes = paramTypes;
            _returnType = returnType;
        }

        public Type[] GetParameterTypes(string methodName)
        {
            if (_paramTypes != null)
            {
                return _paramTypes;
            }
            throw new InvalidOperationException("Unexpected binder call");
        }

        public Type GetReturnType(string invocationId)
        {
            if (_returnType != null)
            {
                return _returnType;
            }
            throw new InvalidOperationException("Unexpected binder call");
        }
    }
}
