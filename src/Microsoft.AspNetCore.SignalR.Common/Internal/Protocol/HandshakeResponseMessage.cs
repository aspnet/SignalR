// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Microsoft.AspNetCore.SignalR.Internal.Protocol
{
    public class HandshakeResponseMessage : HubMessage
    {
        private static readonly HandshakeResponseMessage _empty = new HandshakeResponseMessage(null);

        public string Error { get; }

        public HandshakeResponseMessage(string error)
        {
            Error = error;
        }

        // Static factory methods. Don't want to use constructor overloading because it will break down
        // if you need to send a payload statically-typed as a string. And because a static factory is clearer here
        public static HandshakeResponseMessage WithError(string error) => new HandshakeResponseMessage(error);

        public static HandshakeResponseMessage Empty() => _empty;
    }
}
