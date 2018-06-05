using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.AspNetCore.SignalR.Protocol
{
    public class UploadDoneMessage : HubMessage
    {
        public static readonly UploadDoneMessage Instance = new UploadDoneMessage();

        private UploadDoneMessage()
        {
        }
    }
}
