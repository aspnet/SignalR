using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.Sockets.Http.Internal
{
    public static class NoThrowTaskExtensions
    {
        public static NoThrowAwaiter NoThrow(this Task task)
        {
            return new NoThrowAwaiter(task);
        }
    }

    public struct NoThrowAwaiter : ICriticalNotifyCompletion
    {
        private readonly Task _task;
        public NoThrowAwaiter(Task task) { _task = task; }
        public NoThrowAwaiter GetAwaiter() => this;
        public bool IsCompleted => _task.IsCompleted;
        // Observe exception
        public void GetResult() { _ = _task.Exception; }
        public void OnCompleted(Action continuation) => _task.GetAwaiter().OnCompleted(continuation);
        public void UnsafeOnCompleted(Action continuation) => OnCompleted(continuation);
    }
}
