using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.AspNetCore.SignalR.Protocol
{
    public class ChannelCompleteMessage : HubInvocationMessage
    {
        public string Error { get; }
        public bool HasError { get => Error != null; }
        public ChannelCompleteMessage(string invocationId, string error = null) : base(invocationId)
        {
            Error = error;
        }
    }
}
