// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Microsoft.AspNetCore.SignalR.Internal.Encoders;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;

namespace Microsoft.AspNetCore.SignalR.Internal
{
    public class HubProtocolReaderWriter
    {
        public readonly IHubProtocol HubProtocol;
        public readonly IDataEncoder DataEncoder;

        public string ProtocolName => _hubProtocol.Name;

        public HubProtocolReaderWriter(IHubProtocol hubProtocol, IDataEncoder dataEncoder)
        {
            HubProtocol = hubProtocol;
            DataEncoder = dataEncoder;
        }

        public bool ReadMessages(ReadOnlyBuffer<byte> buffer, IInvocationBinder binder, out IList<HubMessage> messages, out SequencePosition consumed, out SequencePosition examined)
        {
            // TODO: Fix this implementation to be incremental
            consumed = buffer.End;
            examined = consumed;

            return ReadMessages(buffer.ToArray(), binder, out messages);
        }

        public bool ReadMessages(byte[] input, IInvocationBinder binder, out IList<HubMessage> messages)
        {
            messages = new List<HubMessage>();
            ReadOnlySpan<byte> span = input;
            while (span.Length > 0 && DataEncoder.TryDecode(ref span, out var data))
            {
                HubProtocol.TryParseMessages(data, binder, messages);
            }
            return messages.Count > 0;
        }

        public byte[] WriteMessage(HubMessage hubMessage)
        {
            using (var ms = new MemoryStream())
            {
                HubProtocol.WriteMessage(hubMessage, ms);
                return DataEncoder.Encode(ms.ToArray());
            }
        }
    }
}
