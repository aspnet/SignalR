using System;

namespace Microsoft.AspNetCore.SignalR.Client
{
    [Serializable]
    public class HubException : Exception
    {
        public HubException()
        {
        }

        public HubException(string message) : base(message)
        {
        }

        public HubException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}