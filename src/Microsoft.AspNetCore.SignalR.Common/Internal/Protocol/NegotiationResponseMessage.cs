// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Microsoft.AspNetCore.SignalR.Internal.Protocol
{
    public class NegotiationResponseMessage : HubMessage
    {
        private static readonly NegotiationResponseMessage _empty = new NegotiationResponseMessage(null);

        public string Error { get; }

        public NegotiationResponseMessage(string error)
        {
            Error = error;
        }

        // Static factory methods. Don't want to use constructor overloading because it will break down
        // if you need to send a payload statically-typed as a string. And because a static factory is clearer here
        public static NegotiationResponseMessage WithError(string error) => new NegotiationResponseMessage(error);

        public static NegotiationResponseMessage Empty() => _empty;
    }
}
