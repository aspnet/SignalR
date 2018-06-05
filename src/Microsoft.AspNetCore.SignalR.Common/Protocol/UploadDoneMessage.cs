using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.AspNetCore.SignalR.Protocol
{
    public class UploadDoneMessage : HubInvocationMessage
    {
        public UploadDoneMessage(string invocationId) : base(invocationId)
        {
        }
    }
}
