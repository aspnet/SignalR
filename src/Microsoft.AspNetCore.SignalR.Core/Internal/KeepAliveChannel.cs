using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;

namespace Microsoft.AspNetCore.SignalR.Core.Internal
{
    internal class KeepAliveChannel : Channel<HubMessage>
    {
        public KeepAliveChannel(Channel<HubMessage> channel, TimeSpan keepAliveInterval)
        {
            Reader = new KeepAliveReader(channel.Reader, (int)keepAliveInterval.TotalMilliseconds);
            Writer = channel.Writer;
        }

        private class KeepAliveReader : ChannelReader<HubMessage>
        {
            private int _pingQueued = 0;
            private readonly ChannelReader<HubMessage> _reader;
            private readonly int _keepAliveIntervalMs;

            public KeepAliveReader(ChannelReader<HubMessage> reader, int keepAliveIntervalMs)
            {
                _reader = reader;
                _keepAliveIntervalMs = keepAliveIntervalMs;
            }

            public override bool TryRead(out HubMessage item)
            {
                if(Interlocked.CompareExchange(ref _pingQueued, 0, 1) == 1)
                {
                    // There's a ping queued, return that
                    item = PingMessage.Instance;
                    return true;
                }
                else
                {
                    return _reader.TryRead(out item);
                }
            }

            public override Task<bool> WaitToReadAsync(CancellationToken cancellationToken = default)
            {
                var readTask = _reader.WaitToReadAsync();
                if (readTask.IsCompleted)
                {
                    return readTask;
                }
                else
                {
                    return WaitToReadAsyncAwaited(Task.Delay(_keepAliveIntervalMs), readTask);
                }
            }

            private async Task<bool> WaitToReadAsyncAwaited(Task delayTask, Task<bool> readTask)
            {
                var completed = await Task.WhenAny(delayTask, readTask);
                if(ReferenceEquals(completed, readTask))
                {
                    return await readTask;
                }
                else
                {
                    // Indicate that a ping is queued up (so TryRead will return it)
                    Interlocked.Exchange(ref _pingQueued, 1);
                    return true;
                }
            }
        }
    }
}
