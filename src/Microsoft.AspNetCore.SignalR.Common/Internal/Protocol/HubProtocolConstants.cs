using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.AspNetCore.SignalR.Internal.Protocol
{
    internal static class HubProtocolConstants
    {
        public const int InvocationMessageType = 1;
        public const int StreamItemMessageType = 2;
        public const int CompletionMessageType = 3;
        public const int StreamInvocationMessageType = 4;
        public const int CancelInvocationMessageType = 5;
        public const int PingMessageType = 6;
        public const int PongMessageType = 7;
    }
}
