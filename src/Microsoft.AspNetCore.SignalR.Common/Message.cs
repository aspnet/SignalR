﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Microsoft.AspNetCore.Sockets
{
    public struct Message
    {
        public MessageType Type { get; }

        // REVIEW: We need a better primitive to use here. Memory<byte> would be good,
        // but @davidfowl has concerns about allocating OwnedMemory and how to dispose
        // it properly
        public byte[] Payload { get; }

        public Message(byte[] payload, MessageType type)
        {
            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            Type = type;
            Payload = payload;
        }
    }
}
