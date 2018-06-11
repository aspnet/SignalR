using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.AspNetCore.SignalR.Protocol
{
    public class ChannelPlaceholder
    {
        public Type ItemType { get; }
        public ChannelPlaceholder(Type itemType)
        {
            ItemType = itemType;
        }
    }
}
