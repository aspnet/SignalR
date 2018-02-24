// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.AspNetCore.SignalR.Internal.Encoders;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;

namespace Microsoft.AspNetCore.SignalR.Internal.Protocol
{
    public abstract class HubMessage
    {
        protected HubMessage()
        {
        }

        private readonly List<SerializedMessage> _serializedMessages = new List<SerializedMessage>(4);

        public byte[] GetMessage(HubProtocolReaderWriter protocolReaderWriter)
        {
            for (var i = 0; i < _serializedMessages.Count; i++)
            {
                if (_serializedMessages[i].ProtocolReaderWriter.Equals(protocolReaderWriter))
                {
                    return _serializedMessages[i].Message;
                }
            }

            var bytes = protocolReaderWriter.WriteMessage(this);
            _serializedMessages.Add(new SerializedMessage
            {
                Message = bytes,
                ProtocolReaderWriter = protocolReaderWriter
            });

            return bytes;
        }

        private struct SerializedMessage
        {
            public HubProtocolReaderWriter ProtocolReaderWriter;
            public byte[] Message;
        }
    }
}
