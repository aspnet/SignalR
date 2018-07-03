using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.AspNetCore.SignalR.Protocol
{
    public class StreamCompleteMessage : HubMessage
    {
        public string StreamId { get; }
        public string Error { get; }
        public bool HasError { get => Error != null; }
        public StreamCompleteMessage(string streamId, string error = null) 
        {
            StreamId = streamId;
            Error = error;
        }
    }
}
