using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.AspNetCore.SignalR.Protocol
{
    public class ChannelPlaceholder
    {
        public Type ItemType { get; private set; }
        public string StreamId { get; private set; }

        public ChannelPlaceholder(Type itemType, string streamId)
        {
            ItemType = itemType;
            StreamId = streamId;
        }
    }
}
