using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.SignalR.Protocol;

namespace Microsoft.AspNetCore.SignalR.Client.FunctionalTests
{
    public class VersionedJsonHubProtocol : IHubProtocol
    {
        private readonly JsonHubProtocol _innerProtocol;

        public VersionedJsonHubProtocol()
        {
            _innerProtocol = new JsonHubProtocol();
        }

        public string Name => _innerProtocol.Name;
        public int Version => int.MaxValue;
        public TransferFormat TransferFormat => _innerProtocol.TransferFormat;

        public bool TryParseMessage(ref ReadOnlySequence<byte> input, IInvocationBinder binder, out HubMessage message)
        {
            return _innerProtocol.TryParseMessage(ref input, binder, out message);
        }

        public void WriteMessage(HubMessage message, IBufferWriter<byte> output)
        {
            _innerProtocol.WriteMessage(message, output);
        }

        public ReadOnlyMemory<byte> GetMessageBytes(HubMessage message)
        {
            return _innerProtocol.GetMessageBytes(message);
        }

        public bool IsVersionSupported(int version)
        {
            // Support older clients
            return version <= Version;
        }
    }
}