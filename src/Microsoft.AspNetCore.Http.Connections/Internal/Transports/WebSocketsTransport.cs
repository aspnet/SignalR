// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.Logging;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics.Tracing;
using System.Runtime.CompilerServices;

namespace Microsoft.AspNetCore.Http.Connections.Internal.Transports
{
    public partial class WebSocketsTransport : IHttpTransport
    {
        private readonly WebSocketOptions _options;
        private readonly ILogger _logger;
        private readonly IDuplexPipe _application;
        private readonly HttpConnectionContext _connection;
        private volatile bool _aborted;
        private static NagleTimer _nagleTimer = new NagleTimer();

        public WebSocketsTransport(WebSocketOptions options, IDuplexPipe application, HttpConnectionContext connection, ILoggerFactory loggerFactory)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (application == null)
            {
                throw new ArgumentNullException(nameof(application));
            }

            if (loggerFactory == null)
            {
                throw new ArgumentNullException(nameof(loggerFactory));
            }

            _options = options;
            _application = application;
            _connection = connection;
            _logger = loggerFactory.CreateLogger<WebSocketsTransport>();
        }

        public async Task ProcessRequestAsync(HttpContext context, CancellationToken token)
        {
            Debug.Assert(context.WebSockets.IsWebSocketRequest, "Not a websocket request");

            var subProtocol = _options.SubProtocolSelector?.Invoke(context.WebSockets.WebSocketRequestedProtocols);

            using (var ws = await context.WebSockets.AcceptWebSocketAsync(subProtocol))
            {
                Log.SocketOpened(_logger, subProtocol);

                try
                {
                    await ProcessSocketAsync(ws);
                }
                finally
                {
                    Log.SocketClosed(_logger);
                }
            }
        }

        public async Task ProcessSocketAsync(WebSocket socket)
        {
            // Begin sending and receiving. Receiving must be started first because ExecuteAsync enables SendAsync.
            var receiving = StartReceiving(socket);
            var sending = StartSending(socket);

            // Wait for send or receive to complete
            var trigger = await Task.WhenAny(receiving, sending);

            if (trigger == receiving)
            {
                Log.WaitingForSend(_logger);

                // We're waiting for the application to finish and there are 2 things it could be doing
                // 1. Waiting for application data
                // 2. Waiting for a websocket send to complete

                // Cancel the application so that ReadAsync yields
                _application.Input.CancelPendingRead();

                using (var delayCts = new CancellationTokenSource())
                {
                    var resultTask = await Task.WhenAny(sending, Task.Delay(_options.CloseTimeout, delayCts.Token));

                    if (resultTask != sending)
                    {
                        // We timed out so now we're in ungraceful shutdown mode
                        Log.CloseTimedOut(_logger);

                        // Abort the websocket if we're stuck in a pending send to the client
                        _aborted = true;

                        socket.Abort();
                    }
                    else
                    {
                        delayCts.Cancel();
                    }
                }
            }
            else
            {
                Log.WaitingForClose(_logger);

                // We're waiting on the websocket to close and there are 2 things it could be doing
                // 1. Waiting for websocket data
                // 2. Waiting on a flush to complete (backpressure being applied)

                using (var delayCts = new CancellationTokenSource())
                {
                    var resultTask = await Task.WhenAny(receiving, Task.Delay(_options.CloseTimeout, delayCts.Token));

                    if (resultTask != receiving)
                    {
                        // Abort the websocket if we're stuck in a pending receive from the client
                        _aborted = true;

                        socket.Abort();

                        // Cancel any pending flush so that we can quit
                        _application.Output.CancelPendingFlush();
                    }
                    else
                    {
                        delayCts.Cancel();
                    }
                }
            }
        }

        private async Task StartReceiving(WebSocket socket)
        {
            try
            {
                while (true)
                {
#if NETCOREAPP2_1
                    // Do a 0 byte read so that idle connections don't allocate a buffer when waiting for a read
                    var result = await socket.ReceiveAsync(Memory<byte>.Empty, CancellationToken.None);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        return;
                    }
#endif
                    var memory = _application.Output.GetMemory();

#if NETCOREAPP2_1
                    // Because we checked the CloseStatus from the 0 byte read above, we don't need to check again after reading
                    var receiveResult = await socket.ReceiveAsync(memory, CancellationToken.None);
#else
                    var isArray = MemoryMarshal.TryGetArray<byte>(memory, out var arraySegment);
                    Debug.Assert(isArray);

                    // Exceptions are handled above where the send and receive tasks are being run.
                    var receiveResult = await socket.ReceiveAsync(arraySegment, CancellationToken.None);

                    if (receiveResult.MessageType == WebSocketMessageType.Close)
                    {
                        return;
                    }
#endif
                    Log.MessageReceived(_logger, receiveResult.MessageType, receiveResult.Count, receiveResult.EndOfMessage);

                    _application.Output.Advance(receiveResult.Count);

                    var flushResult = await _application.Output.FlushAsync();

                    // We canceled in the middle of applying back pressure
                    // or if the consumer is done
                    if (flushResult.IsCanceled || flushResult.IsCompleted)
                    {
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Ignore aborts, don't treat them like transport errors
            }
            catch (Exception ex)
            {
                if (!_aborted)
                {
                    _application.Output.Complete(ex);

                    // We re-throw here so we can communicate that there was an error when sending
                    // the close frame
                    throw;
                }
            }
            finally
            {
                // We're done writing
                _application.Output.Complete();
            }
        }

        private async Task StartSending(WebSocket socket)
        {
            Exception error = null;
            const int maxBuffer = 4096;
            long totalBufferSize = 0;
            int totalSends = 0;
            int secondLoopRuns = 0;
            double watchTime = 0;
            Stopwatch _watch = new Stopwatch();

            try
            {
                while (true)
                {
                    var result = await _application.Input.ReadAsync();
                    var buffer = result.Buffer;

                    // Get a frame from the application

                    try
                    {
                        if (result.IsCanceled)
                        {
                            break;
                        }

                        if (!buffer.IsEmpty)
                        {
                            try
                            {
                                if (buffer.Length >= maxBuffer)
                                {
                                    Log.SendPayload(_logger, buffer.Length);

                                    var webSocketMessageType = (_connection.ActiveFormat == TransferFormat.Binary
                                    ? WebSocketMessageType.Binary
                                    : WebSocketMessageType.Text);

                                    if (WebSocketCanSend(socket))
                                    {
                                        await socket.SendAsync(buffer, webSocketMessageType);
                                        totalBufferSize += buffer.Length;
                                        totalSends++;
                                    }
                                    else
                                    {
                                        break;
                                    }
                                }
                                else
                                {
                                    while (buffer.Length < maxBuffer)
                                    {
                                        var length = buffer.Length;

                                        _application.Input.AdvanceTo(buffer.Start, buffer.End);

                                        _watch.Start();
                                        //await Task.Delay(1);
                                        //await Task.Yield();
                                        await _nagleTimer;
                                        _watch.Stop();
                                        watchTime += _watch.ElapsedMilliseconds;
                                        _watch.Reset();

                                        var hasData = _application.Input.TryRead(out result);
                                        buffer = result.Buffer;

                                        secondLoopRuns++;

                                        if (buffer.Length == length)
                                        {
                                            break;
                                        }
                                    }

                                    Log.SendPayload(_logger, buffer.Length);

                                    var webSocketMessageType = (_connection.ActiveFormat == TransferFormat.Binary
                                    ? WebSocketMessageType.Binary
                                    : WebSocketMessageType.Text);

                                    if (WebSocketCanSend(socket))
                                    {
                                        await socket.SendAsync(buffer, webSocketMessageType);
                                        totalBufferSize += buffer.Length;
                                        totalSends++;
                                    }
                                    else
                                    {
                                        break;
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                if (!_aborted)
                                {
                                    Log.ErrorWritingFrame(_logger, ex);
                                }
                                break;
                            }
                        }
                        else if (result.IsCompleted)
                        {
                            break;
                        }
                    }
                    finally
                    {
                        _application.Input.AdvanceTo(buffer.End);
                    }
                }
            }
            catch (Exception ex)
            {
                error = ex;
            }
            finally
            {
                // Send the close frame before calling into user code
                if (WebSocketCanSend(socket))
                {
                    // We're done sending, send the close frame to the client if the websocket is still open
                    await socket.CloseOutputAsync(error != null ? WebSocketCloseStatus.InternalServerError : WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                }

                _application.Input.Complete();

                Console.WriteLine($"Total bytes send: {totalBufferSize}");
                Console.WriteLine($"Average bytes per send: {totalBufferSize / totalSends}");
                Console.WriteLine($"Number of times second loop ran: {secondLoopRuns}");
                Console.WriteLine($"Total time Task.Delay took {watchTime}ms");
                Console.WriteLine($"Average time Task.Delay took per iteration {watchTime / secondLoopRuns}ms");
            }

        }

        private static bool WebSocketCanSend(WebSocket ws)
        {
            return !(ws.State == WebSocketState.Aborted ||
                   ws.State == WebSocketState.Closed ||
                   ws.State == WebSocketState.CloseSent);
        }
    }

    class NagleTimer : ICriticalNotifyCompletion
    {
        private DelayScheduler _scheduler = new DelayScheduler();

        public NagleTimer GetAwaiter() => this;
        public bool IsCompleted => false;

        public void GetResult()
        {
        }

        public void OnCompleted(Action continuation)
        {
            _scheduler.Schedule(o => ((Action)o).Invoke(), continuation);
        }

        public void UnsafeOnCompleted(Action continuation)
        {
            OnCompleted(continuation);
        }
    }

    /// <summary>
    /// DelayScheduler will defer things that are Scheduled until 'DelayCount'
    /// other work items have been enqueued.  Thus items are simply delayed in
    /// time.
    /// </summary>
    public class DelayScheduler : SchedulerBase
    {
        public int DelayCount = 6;

        public override void Schedule(Action<object> action, object state)
        {
            SchedulerEventSource.Log.Schedule(this.GetHashCode(), "Delay", _workItems.Count);
            base.Schedule(action, state);

            var currentTick = Environment.TickCount;
            while (_workItems.TryPeek(out var res))
            {
                if (res.QueueTick + 2 < currentTick)
                {
                    if (_workItems.TryDequeue(out var work))
                    {
                        SchedulerEventSource.Log.CallbackStart(this.GetHashCode());
                        work.Callback(work.State);
                        SchedulerEventSource.Log.CallbackStop(this.GetHashCode());
                    }
                }
                else
                    break;
            }
        }
    }

    class SchedulerEventSource : EventSource
    {
        public void FlushStart(int id)
        {
            if (IsEnabled())
                WriteEvent(1, id);
        }
        public void FlushStop(int id)
        {
            if (IsEnabled())
                WriteEvent(2, id);
        }

        public void Schedule(int id, string kind, int num)
        {
            if (IsEnabled())
                WriteEvent(3, id, kind, num);
        }

        public void CallbackStart(int id)
        {
            if (IsEnabled())
                WriteEvent(4, id);
        }

        public void CallbackStop(int id)
        {
            if (IsEnabled())
                WriteEvent(5, id);
        }

        public static SchedulerEventSource Log = new SchedulerEventSource();
    }

    /// <summary>
    /// SchedulerBase implements a very simple PipeScheduler, that basically
    /// defers anything it schedules 20 msec or so into to future.   It however
    /// is not meant to be used itself.  Instead the intent is to subclass it
    /// and override schedule() so that after scheduling an item it might do
    /// some of the work previously scheduled.
    ///
    /// In general the expectation is that in the 'fast' steady-state path you
    /// rarely rely on 20 msec timer to go off.
    /// </summary>
    public class SchedulerBase : PipeScheduler
    {
        public SchedulerBase()
        {
            _timer = new Timer(Timeout, new WeakReference(this), System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
            _lastTimerUpdate = Environment.TickCount - 1000;        // Set the time in the past.  THis insures our optimziation does not kick in.
        }

        ~SchedulerBase()
        {
            Flush();
        }

        public override void Schedule(Action<object> action, object state)
        {
            _workItems.Enqueue(new Work { Callback = action, State = state, QueueTick = Environment.TickCount });
            InsureFlush();
        }

        private static void Timeout(object obj)
        {
            WeakReference reference = (WeakReference)obj;
            SchedulerBase scheduler = reference.Target as SchedulerBase;
            if (scheduler == null)
                return;     // The Scheduler has died, and the timer has not been disposed yet,  Just give up, the timer will die eventually.

            scheduler.Flush();
        }

        private void Flush()
        {
            SchedulerEventSource.Log.FlushStart(this.GetHashCode());
            while (!_workItems.IsEmpty)
            {
                Work itemToRun;
                if (_workItems.TryDequeue(out itemToRun))
                    System.Threading.ThreadPool.QueueUserWorkItem((object state) => itemToRun.Callback(state), itemToRun.State);
            }
            SchedulerEventSource.Log.FlushStop(this.GetHashCode());
        }

        private void InsureFlush()
        {
            // Logically we set the timeout timer every time we schedule, but we optimize
            // by skipping if we have done it in the last 10 msec.
            int curTickCount = Environment.TickCount;
            if ((uint)(curTickCount - _lastTimerUpdate) > 10)
            {
                _lastTimerUpdate = curTickCount;
                lock (_timer)
                    _timer.Change(15, System.Threading.Timeout.Infinite);
            }
        }

        internal readonly ConcurrentQueue<Work> _workItems = new ConcurrentQueue<Work>();
        private readonly Timer _timer;
        private int _lastTimerUpdate;

        internal struct Work
        {
            public Action<object> Callback;
            public object State;
            public long QueueTick;
        }
    }
}
