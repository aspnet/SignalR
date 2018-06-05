using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.AspNetCore.SignalR.Protocol
{
    public class StreamCompleteMessage : HubInvocationMessage
    {
        public StreamCompleteMessage(string invocationId) : base(invocationId)
        {
        }
    }
}
