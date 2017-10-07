// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Microsoft.AspNetCore.SignalR.Test.Server
{
    public class ComplexObject
    {
        public string String { get; set; }
        public int[] IntArray { get; set; }
        // TODO: byte[] currently doesn't roundtrip for msgpack. See: https://github.com/aspnet/SignalR/issues/945#issuecomment-333260762
    }
}