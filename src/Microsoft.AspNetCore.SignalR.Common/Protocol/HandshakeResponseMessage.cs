// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Microsoft.AspNetCore.SignalR.Protocol
{
    /// <summary>
    /// A handshake response message.
    /// </summary>
    public class HandshakeResponseMessage : HubMessage
    {
        /// <summary>
        /// Depreciated.
        /// An empty response message with no error.
        /// </summary>
        public static readonly HandshakeResponseMessage Empty = new HandshakeResponseMessage(error: null);

        /// <summary>
        /// Gets the optional error message.
        /// </summary>
        public string Error { get; }

        /// <summary>
        /// Highest minor protocol version that the server supports.
        /// </summary>
        public int MinorVersion { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="HandshakeResponseMessage"/> class.
        /// If you are intending to send back an error, the minor version doesn't matter.
        /// </summary>
        /// <param name="error"></param>
        public HandshakeResponseMessage(string error) : this(null, error) { }

        public HandshakeResponseMessage(int minorVersion) : this(minorVersion, null) { }

        public HandshakeResponseMessage(IHubProtocol protocol) 
            : this((protocol is IHubProtocol2) ? ((IHubProtocol2)protocol).MinorVersion : 0) { }

        /// <summary>
        /// Initializes a new instance of the <see cref="HandshakeResponseMessage"/> class.
        /// </summary>
        public HandshakeResponseMessage(int? minorVersion, string error)
        {
            // MinorVersion defaults to 0, because old servers don't send a minor version 
            MinorVersion = minorVersion ?? 0;
            Error = error;
        }

    }
}
