// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;

namespace Microsoft.AspNetCore.SignalR.Internal.Protocol
{
    public abstract class HubMessage
    {
        // REVIEW: In theory someone could downcast and write to this... I could make a custom empty dictionary type.
        public static readonly IReadOnlyDictionary<string, string> EmptyHeaders = new Dictionary<string, string>();

        public IReadOnlyDictionary<string, string> Headers { get; }

        protected HubMessage(IReadOnlyDictionary<string, string> headers)
        {
            Headers = headers ?? throw new ArgumentNullException(nameof(headers));
        }
    }
}
