using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.AspNetCore.SignalR.Protocol
{
    public class ChannelPlaceholder
    {
        public Type ItemType { get; private set; }
        public Guid ChannelId { get; private set; }

        public ChannelPlaceholder(Type itemType, Guid channelId)
        {
            ItemType = itemType;
            ChannelId = channelId;
        }
    }
}
