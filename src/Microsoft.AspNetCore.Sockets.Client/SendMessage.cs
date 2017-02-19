using System;
using System.IO.Pipelines;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.Sockets.Client
{
    public struct SendMessage : IDisposable
    {
        public MessageType Type { get; }
        public PreservedBuffer Payload { get; }
        public TaskCompletionSource<bool> Result { get; }

        public SendMessage(PreservedBuffer payload, MessageType type, TaskCompletionSource<bool> result)
        {
            Type = type;
            Payload = payload;
            Result = result;
        }

        public void Dispose()
        {
            Payload.Dispose();
        }
    }
}
