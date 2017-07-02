// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.IO.Pipelines;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Hosting.Server;

namespace Microsoft.AspNetCore.Sockets.HttpServer
{
    public class FrameConnection : ITimeoutControl
    {
        private readonly FrameConnectionContext _context;
        private readonly TaskCompletionSource<object> _socketClosedTcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        private Frame _frame;

        private long _lastTimestamp;
        private long _timeoutTimestamp = long.MaxValue;
        private TimeoutAction _timeoutAction;

        private object _readTimingLock = new object();
        private bool _readTimingEnabled;
        private bool _readTimingPauseRequested;
        private long _readTimingElapsedTicks;
        private long _readTimingBytesRead;

        private Task _lifetimeTask;

        public FrameConnection(FrameConnectionContext context)
        {
            _context = context;
        }

        // For testing
        internal Frame Frame => _frame;
        internal IDebugger Debugger { get; set; } = DebuggerWrapper.Singleton;


        public bool TimedOut { get; private set; }

        public string ConnectionId => _context.ConnectionId;
        public IPipeWriter Input => _context.Input.Writer;
        public IPipeReader Output => _context.Output.Reader;

        // Internal for testing
        internal PipeOptions AdaptedInputPipeOptions => new PipeOptions
        {
            ReaderScheduler = TaskRunScheduler.Default,
            WriterScheduler = InlineScheduler.Default
        };

        internal PipeOptions AdaptedOutputPipeOptions => new PipeOptions
        {
            ReaderScheduler = InlineScheduler.Default,
            WriterScheduler = InlineScheduler.Default,
        };

        private IKestrelTrace Log => _context.ServiceContext.Log;

        public void StartRequestProcessing<TContext>(IHttpApplication<TContext> application)
        {
            _lifetimeTask = ProcessRequestsAsync<TContext>(application);
        }

        private async Task ProcessRequestsAsync<TContext>(IHttpApplication<TContext> application)
        {
            try
            {
                Log.ConnectionStart(ConnectionId);
                KestrelEventSource.Log.ConnectionStart(null);

                var input = _context.Input.Reader;
                var output = _context.Output;

                // _frame must be initialized before adding the connection to the connection manager
                CreateFrame(application, input, output);

                // Do this before the first await so we don't yield control to the transport until we've
                // added the connection to the connection manager
                _context.ServiceContext.ConnectionManager.AddConnection(_context.FrameConnectionId, this);
                _lastTimestamp = _context.ServiceContext.SystemClock.UtcNow.Ticks;

                await _frame.ProcessRequestsAsync();
                await _socketClosedTcs.Task;
            }
            catch (Exception ex)
            {
                Log.LogError(0, ex, $"Unexpected exception in {nameof(FrameConnection)}.{nameof(ProcessRequestsAsync)}.");
            }
            finally
            {
                _context.ServiceContext.ConnectionManager.RemoveConnection(_context.FrameConnectionId);

                if (_frame.WasUpgraded)
                {
                    _context.ServiceContext.ConnectionManager.UpgradedConnectionCount.ReleaseOne();
                }
                else
                {
                    _context.ServiceContext.ConnectionManager.NormalConnectionCount.ReleaseOne();
                }

                Log.ConnectionStop(ConnectionId);
                KestrelEventSource.Log.ConnectionStop(null);
            }
        }

        internal void CreateFrame<TContext>(IHttpApplication<TContext> application, IPipeReader input, IPipe output)
        {
            _frame = new Frame<TContext>(application, new FrameContext
            {
                ConnectionId = _context.ConnectionId,
                ServiceContext = _context.ServiceContext,
                TimeoutControl = this,
                Input = input,
                Output = output.Writer
            });
        }

        public void OnConnectionClosed(Exception ex)
        {
            Debug.Assert(_frame != null, $"{nameof(_frame)} is null");

            // Abort the connection (if not already aborted)
            _frame.Abort(ex);

            _socketClosedTcs.TrySetResult(null);
        }

        public Task StopAsync()
        {
            Debug.Assert(_frame != null, $"{nameof(_frame)} is null");

            _frame.Stop();

            return _lifetimeTask;
        }

        public void Abort(Exception ex)
        {
            Debug.Assert(_frame != null, $"{nameof(_frame)} is null");

            // Abort the connection (if not already aborted)
            _frame.Abort(ex);
        }

        public Task AbortAsync(Exception ex)
        {
            Debug.Assert(_frame != null, $"{nameof(_frame)} is null");

            // Abort the connection (if not already aborted)
            _frame.Abort(ex);

            return _lifetimeTask;
        }

        public void SetTimeoutResponse()
        {
            Debug.Assert(_frame != null, $"{nameof(_frame)} is null");

            _frame.SetBadRequestState(RequestRejectionReason.RequestTimeout);
        }

        public void Timeout()
        {
            Debug.Assert(_frame != null, $"{nameof(_frame)} is null");

            TimedOut = true;
            _readTimingEnabled = false;
            _frame.Stop();
        }

        public void Tick(DateTimeOffset now)
        {
            Debug.Assert(_frame != null, $"{nameof(_frame)} is null");

            var timestamp = now.Ticks;

            // TODO: Use PlatformApis.VolatileRead equivalent again
            if (timestamp > Interlocked.Read(ref _timeoutTimestamp))
            {
                if (!Debugger.IsAttached)
                {
                    CancelTimeout();

                    if (_timeoutAction == TimeoutAction.SendTimeoutResponse)
                    {
                        SetTimeoutResponse();
                    }

                    Timeout();
                }
            }
            else
            {
                lock (_readTimingLock)
                {
                    if (_readTimingEnabled)
                    {
                        // Reference in local var to avoid torn reads in case the min rate is changed via IHttpMinRequestBodyDataRateFeature
                        var minRequestBodyDataRate = _frame.MinRequestBodyDataRate;

                        _readTimingElapsedTicks += timestamp - _lastTimestamp;

                        if (minRequestBodyDataRate?.BytesPerSecond > 0 && _readTimingElapsedTicks > minRequestBodyDataRate.GracePeriod.Ticks)
                        {
                            var elapsedSeconds = (double)_readTimingElapsedTicks / TimeSpan.TicksPerSecond;
                            var rate = Interlocked.Read(ref _readTimingBytesRead) / elapsedSeconds;

                            if (rate < minRequestBodyDataRate.BytesPerSecond && !Debugger.IsAttached)
                            {
                                Log.RequestBodyMininumDataRateNotSatisfied(_context.ConnectionId, _frame.TraceIdentifier, minRequestBodyDataRate.BytesPerSecond);
                                Timeout();
                            }
                        }

                        // PauseTimingReads() cannot just set _timingReads to false. It needs to go through at least one tick
                        // before pausing, otherwise _readTimingElapsed might never be updated if PauseTimingReads() is always
                        // called before the next tick.
                        if (_readTimingPauseRequested)
                        {
                            _readTimingEnabled = false;
                            _readTimingPauseRequested = false;
                        }
                    }
                }
            }

            Interlocked.Exchange(ref _lastTimestamp, timestamp);
        }

        public void SetTimeout(long ticks, TimeoutAction timeoutAction)
        {
            Debug.Assert(_timeoutTimestamp == long.MaxValue, "Concurrent timeouts are not supported");

            AssignTimeout(ticks, timeoutAction);
        }

        public void ResetTimeout(long ticks, TimeoutAction timeoutAction)
        {
            AssignTimeout(ticks, timeoutAction);
        }

        public void CancelTimeout()
        {
            Interlocked.Exchange(ref _timeoutTimestamp, long.MaxValue);
        }

        private void AssignTimeout(long ticks, TimeoutAction timeoutAction)
        {
            _timeoutAction = timeoutAction;

            // Add Heartbeat.Interval since this can be called right before the next heartbeat.
            Interlocked.Exchange(ref _timeoutTimestamp, _lastTimestamp + ticks + Heartbeat.Interval.Ticks);
        }

        public void StartTimingReads()
        {
            lock (_readTimingLock)
            {
                _readTimingElapsedTicks = 0;
                _readTimingBytesRead = 0;
                _readTimingEnabled = true;
            }
        }

        public void StopTimingReads()
        {
            lock (_readTimingLock)
            {
                _readTimingEnabled = false;
            }
        }

        public void PauseTimingReads()
        {
            lock (_readTimingLock)
            {
                _readTimingPauseRequested = true;
            }
        }

        public void ResumeTimingReads()
        {
            lock (_readTimingLock)
            {
                _readTimingEnabled = true;

                // In case pause and resume were both called between ticks
                _readTimingPauseRequested = false;
            }
        }

        public void BytesRead(int count)
        {
            Interlocked.Add(ref _readTimingBytesRead, count);
        }
    }
}
