// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;

namespace Microsoft.AspNetCore.SignalR.Internal.Protocol
{
    public abstract class HubMessage
    {
        private Dictionary<string, string> _headers;

        public IDictionary<string, string> Headers
        {
            get
            {
                return _headers ?? (_headers = new Dictionary<string, string>());
            }
        }

        protected HubMessage()
        {
        }
    }
}
