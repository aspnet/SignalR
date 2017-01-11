﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Binary;
using System.IO.Pipelines;
using System.IO.Pipelines.Text.Primitives;
using System.Text;
using System.Text.Formatting;

namespace Microsoft.Extensions.WebSockets.Internal
{
    /// <summary>
    /// Represents the payload of a Close frame (i.e. a <see cref="WebSocketFrame"/> with an <see cref="WebSocketFrame.Opcode"/> of <see cref="WebSocketOpcode.Close"/>).
    /// </summary>
    public struct WebSocketCloseResult
    {
        internal static WebSocketCloseResult AbnormalClosure = new WebSocketCloseResult(WebSocketCloseStatus.AbnormalClosure, "Underlying transport connection was terminated");
        internal static WebSocketCloseResult Empty = new WebSocketCloseResult(WebSocketCloseStatus.Empty);

        /// <summary>
        /// Gets the close status code specified in the frame.
        /// </summary>
        public WebSocketCloseStatus Status { get; }

        /// <summary>
        /// Gets the close status description specified in the frame.
        /// </summary>
        public string Description { get; }

        public WebSocketCloseResult(WebSocketCloseStatus status) : this(status, string.Empty) { }
        public WebSocketCloseResult(WebSocketCloseStatus status, string description)
        {
            Status = status;
            Description = description;
        }

        public int GetSize() => Encoding.UTF8.GetByteCount(Description) + sizeof(ushort);

        public static bool TryParse(ReadableBuffer payload, out WebSocketCloseResult result, out ushort? actualCloseCode)
        {
            if (payload.Length == 0)
            {
                // Empty payload is OK
                actualCloseCode = null;
                result = new WebSocketCloseResult(WebSocketCloseStatus.Empty, string.Empty);
                return true;
            }
            else if (payload.Length < 2)
            {
                actualCloseCode = null;
                result = default(WebSocketCloseResult);
                return false;
            }
            else
            {
                var status = payload.ReadBigEndian<ushort>();
                actualCloseCode = status;
                var description = string.Empty;
                payload = payload.Slice(2);
                if (payload.Length > 0)
                {
                    description = payload.GetUtf8String();
                }
                result = new WebSocketCloseResult((WebSocketCloseStatus)status, description);
                return true;
            }
        }

        public void WriteTo(ref WritableBuffer buffer)
        {
            buffer.WriteBigEndian((ushort)Status);
            if (!string.IsNullOrEmpty(Description))
            {
                buffer.Append(Description, EncodingData.InvariantUtf8);
            }
        }
    }
}
