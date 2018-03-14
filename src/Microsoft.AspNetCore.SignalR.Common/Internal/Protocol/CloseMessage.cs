// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;

namespace Microsoft.AspNetCore.SignalR.Internal.Protocol
{
    public class CloseMessage : HubMessage
    {
        private static readonly CloseMessage _empty = new CloseMessage(null);

        public string Error { get; }

        public CloseMessage(string error)
        {
            Error = error;
        }

        // Static factory methods. Don't want to use constructor overloading because it will break down
        // if you need to send a payload statically-typed as a string. And because a static factory is clearer here
        public static CloseMessage WithError(string error) => new CloseMessage(error);

        public static CloseMessage Empty() => _empty;
    }
}
