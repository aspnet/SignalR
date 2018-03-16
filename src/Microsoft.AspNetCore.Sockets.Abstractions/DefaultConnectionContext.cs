// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Protocols;
using Microsoft.AspNetCore.Protocols.Features;

namespace Microsoft.AspNetCore.Sockets
{
    public class DefaultConnectionContext : ConnectionContext,
                                            IConnectionIdFeature,
                                            IConnectionItemsFeature,
                                            IConnectionTransportFeature,
                                            IApplicationTransportFeature,
                                            IConnectionUserFeature,
                                            IConnectionHeartbeatFeature,
                                            ITransferFormatFeature
    {
        private object _heartBeatLock = new object();
        private List<(Action<object> handler, object state)> _heartbeatHandlers;
        private PipeOptions _transportPipeOptions;
        private PipeOptions _applicationPipeOptions;
        private IDuplexPipe _transportPipe;
        private IDuplexPipe _applicationPipe;

        // This tcs exists so that multiple calls to DisposeAsync all wait asynchronously
        // on the same task
        private TaskCompletionSource<object> _disposeTcs = new TaskCompletionSource<object>();

        public DefaultConnectionContext(string id)
            : this(id, PipeOptions.Default, PipeOptions.Default)
        {
        }

        public DefaultConnectionContext(string id, PipeOptions transportPipeOptions, PipeOptions applicationPipeOptions)
        {
            _transportPipeOptions = transportPipeOptions;
            _applicationPipeOptions = applicationPipeOptions;
            ConnectionId = id;
            LastSeenUtc = DateTime.UtcNow;

            // The default behavior is that both formats are supported.
            SupportedFormats = TransferFormat.Binary | TransferFormat.Text;
            ActiveFormat = TransferFormat.Text;

            // PERF: This type could just implement IFeatureCollection
            Features = new FeatureCollection();
            Features.Set<IConnectionUserFeature>(this);
            Features.Set<IConnectionItemsFeature>(this);
            Features.Set<IConnectionIdFeature>(this);
            Features.Set<IConnectionTransportFeature>(this);
            Features.Set<IApplicationTransportFeature>(this);
            Features.Set<IConnectionHeartbeatFeature>(this);
            Features.Set<ITransferFormatFeature>(this);
        }

        public CancellationTokenSource Cancellation { get; set; }

        public SemaphoreSlim Lock { get; } = new SemaphoreSlim(1, 1);

        public Task TransportTask { get; set; }

        public Task ApplicationTask { get; set; }

        public DateTime LastSeenUtc { get; set; }

        public ConnectionStatus Status { get; set; } = ConnectionStatus.Inactive;

        public override string ConnectionId { get; set; }

        public override IFeatureCollection Features { get; }

        public ClaimsPrincipal User { get; set; }

        public override IDictionary<object, object> Items { get; set; } = new ConnectionMetadata();

        public IDuplexPipe Application
        {
            get
            {
                if (_transportPipe == null)
                {
                    lock (_heartBeatLock)
                    {
                        if (_transportPipe == null)
                        {
                            var duplexPipe = DuplexPipe.CreateConnectionPair(_transportPipeOptions, _applicationPipeOptions);
                            _transportPipe = duplexPipe.Application;
                            _applicationPipe = duplexPipe.Transport;
                        }
                    }
                }
                return _transportPipe;
            }
            set
            {
                _transportPipe = value;
            }
        }

        public override IDuplexPipe Transport
        {
            get
            {
                if (_applicationPipe == null)
                {
                    lock (_heartBeatLock)
                    {
                        if (_applicationPipe == null)
                        {
                            var duplexPipe = DuplexPipe.CreateConnectionPair(_transportPipeOptions, _applicationPipeOptions);
                            _transportPipe = duplexPipe.Application;
                            _applicationPipe = duplexPipe.Transport;
                        }
                    }
                }
                return _applicationPipe;
            }
            set
            {
                _applicationPipe = value;
            }
        }

        public TransferFormat SupportedFormats { get; set; }

        public TransferFormat ActiveFormat { get; set; }

        public void OnHeartbeat(Action<object> action, object state)
        {
            lock (_heartBeatLock)
            {
                if (_heartbeatHandlers == null)
                {
                    _heartbeatHandlers = new List<(Action<object> handler, object state)>();
                }
                _heartbeatHandlers.Add((action, state));
            }
        }

        public void TickHeartbeat()
        {
            lock (_heartBeatLock)
            {
                if (_heartbeatHandlers == null)
                {
                    return;
                }

                foreach (var (handler, state) in _heartbeatHandlers)
                {
                    handler(state);
                }
            }
        }

        public async Task DisposeAsync()
        {
            Task disposeTask = Task.CompletedTask;

            try
            {
                await Lock.WaitAsync();

                if (Status == ConnectionStatus.Disposed)
                {
                    disposeTask = _disposeTcs.Task;
                }
                else
                {
                    Status = ConnectionStatus.Disposed;

                    // If the application task is faulted, propagate the error to the transport
                    if (ApplicationTask?.IsFaulted == true)
                    {
                        Transport.Output.Complete(ApplicationTask.Exception.InnerException);
                    }
                    else
                    {
                        Transport.Output.Complete();
                    }

                    // If the transport task is faulted, propagate the error to the application
                    if (TransportTask?.IsFaulted == true)
                    {
                        Application.Output.Complete(TransportTask.Exception.InnerException);
                    }
                    else
                    {
                        Application.Output.Complete();
                    }

                    var applicationTask = ApplicationTask ?? Task.CompletedTask;
                    var transportTask = TransportTask ?? Task.CompletedTask;

                    disposeTask = WaitOnTasks(applicationTask, transportTask);
                }
            }
            finally
            {
                Lock.Release();
            }

            try
            {
                await disposeTask;
            }
            finally
            {
                // REVIEW: Should we move this to the read loops?

                // Complete the reading side of the pipes
                Application.Input.Complete();
                Transport.Input.Complete();
            }
        }

        private async Task WaitOnTasks(Task applicationTask, Task transportTask)
        {
            try
            {
                await Task.WhenAll(applicationTask, transportTask);

                // Notify all waiters that we're done disposing
                _disposeTcs.TrySetResult(null);
            }
            catch (OperationCanceledException)
            {
                _disposeTcs.TrySetCanceled();

                throw;
            }
            catch (Exception ex)
            {
                _disposeTcs.TrySetException(ex);

                throw;
            }
        }

        public enum ConnectionStatus
        {
            Inactive,
            Active,
            Disposed
        }
    }
}
