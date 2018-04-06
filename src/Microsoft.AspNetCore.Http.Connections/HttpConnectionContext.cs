// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Http.Connections.Features;
using Microsoft.AspNetCore.Http;

namespace Microsoft.AspNetCore.Http.Connections
{
    public class HttpConnectionContext : ConnectionContext,
                                         IConnectionIdFeature,
                                         IConnectionItemsFeature,
                                         IConnectionTransportFeature,
                                         IApplicationTransportFeature,
                                         IConnectionUserFeature,
                                         IConnectionHeartbeatFeature,
                                         ITransferFormatFeature,
                                         IHttpContextFeature
    {
        private readonly object _heartbeatLock = new object();
        private List<(Action<object> handler, object state)> _heartbeatHandlers;

        // This tcs exists so that multiple calls to DisposeAsync all wait asynchronously
        // on the same task
        private readonly TaskCompletionSource<object> _disposeTcs = new TaskCompletionSource<object>();

        /// <summary>
        /// Creates the DefaultConnectionContext without Pipes to avoid upfront allocations.
        /// The caller is expected to set the <see cref="Transport"/> and <see cref="Application"/> pipes manually.
        /// </summary>
        /// <param name="id"></param>
        public HttpConnectionContext(string id)
        {
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
            Features.Set<IHttpContextFeature>(this);
        }

        public HttpConnectionContext(string id, IDuplexPipe transport, IDuplexPipe application)
            : this(id)
        {
            Transport = transport;
            Application = application;
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

        public override IDictionary<object, object> Items { get; set; } = new ConnectionItems(new ConcurrentDictionary<object, object>());

        public IDuplexPipe Application { get; set; }

        public override IDuplexPipe Transport { get; set; }

        public TransferFormat SupportedFormats { get; set; }

        public TransferFormat ActiveFormat { get; set; }

        public HttpContext HttpContext { get; set; }

        public void OnHeartbeat(Action<object> action, object state)
        {
            lock (_heartbeatLock)
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
            lock (_heartbeatLock)
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

        public async Task DisposeAsync(bool closeGracefully = false)
        {
            var disposeTask = Task.CompletedTask;

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

                    var applicationTask = ApplicationTask ?? Task.CompletedTask;
                    var transportTask = TransportTask ?? Task.CompletedTask;

                    disposeTask = WaitOnTasks(applicationTask, transportTask, closeGracefully);
                }
            }
            finally
            {
                Lock.Release();
            }

            await disposeTask;
        }

        private async Task WaitOnTasks(Task applicationTask, Task transportTask, bool closeGracefully)
        {
            try
            {
                // Closing gracefully means we're only going to close the finished sides of the pipe
                // If the application finishes, that means it's done with the transport pipe
                // If the transport finishes, that means it's done with the application pipe
                if (closeGracefully)
                {
                    // Wait for either to finish
                    var result = await Task.WhenAny(applicationTask, transportTask);

                    // If the application is complete, complete the transport pipe (it's the pipe to the transport)
                    if (result == applicationTask)
                    {
                        Transport.Output.Complete(applicationTask.Exception?.InnerException);
                        Transport.Input.Complete();

                        try
                        {
                            // Transports are written by us and are well behaved, wait for them to drain
                            await transportTask;
                        }
                        finally
                        {
                            // Now complete the application
                            Application.Output.Complete();
                            Application.Input.Complete();
                        }
                    }
                    else
                    {
                        // If the transport is complete, complete the application pipes
                        Application.Output.Complete(transportTask.Exception?.InnerException);
                        Application.Input.Complete();

                        try
                        {
                            // A poorly written application *could* in theory hang forever and it'll show up as a memory leak
                            await applicationTask;
                        }
                        finally
                        {
                            Transport.Output.Complete();
                            Transport.Input.Complete();
                        }
                    }
                }
                else
                {
                    // Shutdown both sides and wait for nothing
                    Transport.Output.Complete(applicationTask.Exception?.InnerException);
                    Application.Output.Complete(transportTask.Exception?.InnerException);

                    try
                    {
                        // A poorly written application *could* in theory hang forever and it'll show up as a memory leak
                        await Task.WhenAll(applicationTask, transportTask);
                    }
                    finally
                    {
                        // Close the reading side after both sides run
                        Application.Input.Complete();
                        Transport.Input.Complete();
                    }
                }

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
