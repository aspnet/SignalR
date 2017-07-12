// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Channels;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Sockets.Client;
using Microsoft.AspNetCore.Sockets.Internal.Formatters;
using Newtonsoft.Json;
using Xunit;

namespace Microsoft.AspNetCore.SignalR.Client.Tests
{
    internal class TestConnection : IConnection
    {
        private TaskCompletionSource<object> _started = new TaskCompletionSource<object>();
        private TaskCompletionSource<object> _disposed = new TaskCompletionSource<object>();

        private Channel<byte[]> _sentMessages = Channel.CreateUnbounded<byte[]>();
        private Channel<byte[]> _receivedMessages = Channel.CreateUnbounded<byte[]>();

        private CancellationTokenSource _receiveShutdownToken = new CancellationTokenSource();
        private Task _receiveLoop;

        public event Func<Task> Connected;
        public event Func<byte[], Task> Received;
        public event Func<Exception, Task> Closed;

        public Task Started => _started.Task;
        public Task Disposed => _disposed.Task;
        public ReadableChannel<byte[]> SentMessages => _sentMessages.In;
        public WritableChannel<byte[]> ReceivedMessages => _receivedMessages.Out;

        public IFeatureCollection Features => throw new NotImplementedException();

        public TestConnection()
        {
            _receiveLoop = ReceiveLoopAsync(_receiveShutdownToken.Token);
        }

        public Task DisposeAsync()
        {
            _disposed.TrySetResult(null);
            _receiveShutdownToken.Cancel();
            return _receiveLoop;
        }

        public async Task SendAsync(byte[] data, CancellationToken cancellationToken)
        {
            if (!_started.Task.IsCompleted)
            {
                throw new InvalidOperationException("Connection must be started before SendAsync can be called");
            }

            while (await _sentMessages.Out.WaitToWriteAsync(cancellationToken))
            {
                if (_sentMessages.Out.TryWrite(data))
                {
                    return;
                }
            }
            throw new ObjectDisposedException("Unable to send message, underlying channel was closed");
        }

        public Task StartAsync()
        {
            _started.TrySetResult(null);
            Connected?.Invoke();
            return Task.CompletedTask;
        }

        public async Task<string> ReadSentTextMessageAsync()
        {
            var message = await SentMessages.ReadAsync();
            return Encoding.UTF8.GetString(message);
        }

        public Task ReceiveJsonMessage(object jsonObject)
        {
            var json = JsonConvert.SerializeObject(jsonObject, Formatting.None);
            var bytes = FormatMessageToArray(Encoding.UTF8.GetBytes(json));

            return _receivedMessages.Out.WriteAsync(bytes);
        }

        private byte[] FormatMessageToArray(byte[] message)
        {
            var output = new MemoryStream();
            TextMessageFormatter.WriteMessage(message, output);
            return output.ToArray();
        }

        private async Task ReceiveLoopAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    while (await _receivedMessages.In.WaitToReadAsync(token))
                    {
                        while (_receivedMessages.In.TryRead(out var message))
                        {
                            await Received?.Invoke(message);
                        }
                    }
                }
                Closed?.Invoke(null);
            }
            catch (OperationCanceledException)
            {
                // Do nothing, we were just asked to shut down.
                Closed?.Invoke(null);
            }
            catch (Exception ex)
            {
                Closed?.Invoke(ex);
            }
        }
    }
}
