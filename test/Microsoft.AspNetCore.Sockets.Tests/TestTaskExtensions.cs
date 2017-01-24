using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.Sockets.Tests
{
    public static class TestTaskExtensions
    {
        public static Task<T> WithTimeout<T>(this Task<T> self) => WithTimeout(self, TimeSpan.FromSeconds(5));

        public static Task<T> WithTimeout<T>(this Task<T> self, int millisecondsTimeout) => WithTimeout(self, TimeSpan.FromMilliseconds(millisecondsTimeout));

        public static async Task<T> WithTimeout<T>(this Task<T> self, TimeSpan timeout)
        {
            var task = WithTimeout((Task)self, timeout);
            await task;

            // If we got here, it means the cancellation task didn't fire.
            return self.GetAwaiter().GetResult();
        }

        public static Task WithTimeout(this Task self) => WithTimeout(self, TimeSpan.FromSeconds(5));

        public static Task WithTimeout(this Task self, int millisecondsTimeout) => WithTimeout(self, TimeSpan.FromMilliseconds(millisecondsTimeout));

        public static async Task WithTimeout(this Task self, TimeSpan timeout)
        {
            var cts = new CancellationTokenSource();
            var tcs = new TaskCompletionSource<object>();

            using (cts.Token.Register(() => tcs.TrySetCanceled()))
            {
                var tasks = Task.WhenAny(self, tcs.Task);
                cts.CancelAfter(timeout);
                var completed = await tasks;
                try
                {
                    completed.GetAwaiter().GetResult();
                }
                catch (OperationCanceledException)
                {
                    throw new TimeoutException("Task timed out");
                }
            }
        }
    }
}
