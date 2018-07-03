using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.AspNetCore.SignalR.Protocol
{
    public class StreamPlaceholder
    {
        public string StreamId { get; private set; }

        public StreamPlaceholder(string streamId)
        {
            StreamId = streamId;
        }
    }
}
