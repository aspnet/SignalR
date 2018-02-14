// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.IO.Pipelines;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR.Internal.Formatters;
using Microsoft.AspNetCore.Sockets;
using Microsoft.AspNetCore.Sockets.Client;
using Microsoft.AspNetCore.Sockets.Features;
using Newtonsoft.Json;

namespace Microsoft.AspNetCore.SignalR.Client.Tests
{
    internal class TestConnection : IConnection
    {
        private TaskCompletionSource<object> _started = new TaskCompletionSource<object>();
        private TaskCompletionSource<object> _disposed = new TaskCompletionSource<object>();

        private TransferMode? _transferMode;

        private IDuplexPipe _transport;
        private IDuplexPipe _application;

        public event Action<Exception> Closed;
        public Task Started => _started.Task;
        public Task Disposed => _disposed.Task;

        private bool _closed;
        private object _closedLock = new object();

        public IFeatureCollection Features { get; } = new FeatureCollection();

        public IDuplexPipe Application => _application;

        public PipeReader Input => _transport.Input;

        public PipeWriter Output => _transport.Output;

        public TestConnection(TransferMode? transferMode = null)
        {
            _transferMode = transferMode;
            var options = new PipeOptions(readerScheduler: PipeScheduler.ThreadPool);
            var pair = DuplexPipe.CreateConnectionPair(options, options);
            _transport = pair.Transport;
            _application = pair.Application;

            Input.OnWriterCompleted((error, state) =>
            {
                TriggerClosed(error);
            },
            null);
        }

        public Task AbortAsync(Exception ex) => DisposeCoreAsync(ex);
        public Task DisposeAsync() => DisposeCoreAsync();

        // TestConnection isn't restartable
        public Task StopAsync() => DisposeAsync();

        private Task DisposeCoreAsync(Exception ex = null)
        {
            Application.Output.Complete(ex);
            Application.Input.Complete();

            return Task.CompletedTask;
        }

        public Task StartAsync()
        {
            if (_transferMode.HasValue)
            {
                var transferModeFeature = Features.Get<ITransferModeFeature>();
                if (transferModeFeature == null)
                {
                    transferModeFeature = new TransferModeFeature();
                    Features.Set(transferModeFeature);
                }

                transferModeFeature.TransferMode = _transferMode.Value;
            }

            _started.TrySetResult(null);
            return Task.CompletedTask;
        }

        public async Task<string> ReadSentTextMessageAsync()
        {
            var message = await _application.Input.ReadSingleAsync();
            return Encoding.UTF8.GetString(message);
        }

        public Task ReceiveJsonMessage(object jsonObject)
        {
            var json = JsonConvert.SerializeObject(jsonObject, Formatting.None);
            var bytes = FormatMessageToArray(Encoding.UTF8.GetBytes(json));

            return _application.Output.WriteAsync(bytes);
        }

        private byte[] FormatMessageToArray(byte[] message)
        {
            var output = new MemoryStream();
            TextMessageFormatter.WriteMessage(message, output);
            return output.ToArray();
        }

        private void TriggerClosed(Exception ex = null)
        {
            lock (_closedLock)
            {
                if (!_closed)
                {
                    _closed = true;
                    Closed?.Invoke(ex);
                }
            }
        }
        public void Dispose()
        {

        }
    }
}
