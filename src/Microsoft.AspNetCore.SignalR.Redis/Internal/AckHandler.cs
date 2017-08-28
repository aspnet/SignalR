using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.SignalR.Redis.Internal
{
    internal class AckHandler : IDisposable
    {
        private readonly ConcurrentDictionary<int, AckInfo> _acks = new ConcurrentDictionary<int, AckInfo>();
        private readonly Timer _timer;
        private readonly TimeSpan _ackThreshold = TimeSpan.FromSeconds(30);
        private readonly TimeSpan _ackInterval = TimeSpan.FromSeconds(5);

        public AckHandler()
        {
            _timer = new Timer(_ => CheckAcks(), state: null, dueTime: _ackInterval, period: _ackInterval);
        }

        public Task CreateAck(int id)
        {
            return _acks.GetOrAdd(id, _ => new AckInfo()).Tcs.Task;
        }

        public bool TriggerAck(int id)
        {
            if (_acks.TryRemove(id, out var ack))
            {
                ack.Tcs.TrySetResult(null);
                return true;
            }

            return false;
        }

        private void CheckAcks()
        {
            var utcNow = DateTime.UtcNow;

            foreach (var pair in _acks)
            {
                var elapsed = utcNow - pair.Value.Created;
                if (elapsed > _ackThreshold)
                {
                    if (_acks.TryRemove(pair.Key, out var ack))
                    {
                        ack.Tcs.TrySetCanceled();
                    }
                }
            }
        }

        public void Dispose()
        {
            _timer.Dispose();

            foreach (var pair in _acks)
            {
                if (_acks.TryRemove(pair.Key, out var ack))
                {
                    ack.Tcs.TrySetCanceled();
                }
            }
        }
    }

    internal class AckInfo
    {
        public TaskCompletionSource<object> Tcs { get; private set; }
        public DateTime Created { get; private set; }

        public AckInfo()
        {
            Created = DateTime.UtcNow;
            Tcs = new TaskCompletionSource<object>(TaskContinuationOptions.RunContinuationsAsynchronously);
        }
    }

    internal enum GroupAction
    {
        Remove,
        Add,
        Ack
    }
}