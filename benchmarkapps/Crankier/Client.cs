// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace Microsoft.AspNetCore.SignalR.Crankier
{
    public class Client
    {
        private HubConnection _connection;
        private CancellationTokenSource _sendCts;
        private bool _sendInProgress;
        private volatile ConnectionState _connectionState = ConnectionState.Connecting;

        public ConnectionState State => _connectionState;
        public async Task CreateAndStartConnectionAsync(string url, HttpTransportType transportType)
        {
            _connection = new HubConnectionBuilder()
                .WithUrl(url, options => options.Transports = transportType)
                .Build();

            _connection.Closed += (ex) =>
            {
                if (ex == null)
                {
                    Trace.WriteLine("Connection terminated");
                    _connectionState = ConnectionState.Disconnected;
                }
                else
                {
                    Trace.WriteLine($"Connection terminated with error: {ex.GetType()}: {ex.Message}");
                    _connectionState = ConnectionState.Faulted;
                }

                return Task.CompletedTask;
            };

            _sendCts = new CancellationTokenSource();

            await ConnectAsync();
        }

        private async Task ConnectAsync()
        {
            for (int connectCount = 0; connectCount <= 3; connectCount++)
            {
                try
                {
                    await _connection.StartAsync();
                    _connectionState = ConnectionState.Connected;
                    break;
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"Connection.Start Failed: {ex.GetType()}: {ex.Message}");

                    if (connectCount == 3)
                    {
                        _connectionState = ConnectionState.Faulted;
                        throw;
                    }
                }

                await Task.Delay(1000);
            }
        }

        public void StartTest(int sendSize, TimeSpan sendInterval)
        {
            var payload = (sendSize == 0) ? String.Empty : new string('a', sendSize);

            if (_sendInProgress)
            {
                _sendCts.Cancel();
                _sendCts = new CancellationTokenSource();
            }
            else
            {
                _sendInProgress = true;
            }

            if (!String.IsNullOrEmpty(payload))
            {
                _ = Task.Run(async () =>
                {
                    while (!_sendCts.Token.IsCancellationRequested && State != ConnectionState.Disconnected)
                    {
                        try
                        {
                            await _connection.InvokeAsync("SendPayload", payload, _sendCts.Token);
                        }
                        // REVIEW: This is bad. We need a way to detect a closed connection when an Invocation fails!
                        catch (InvalidOperationException)
                        {
                            // The connection was closed.
                            Trace.WriteLine("Connection closed");
                            break;
                        }
                        catch (OperationCanceledException)
                        {
                            // The connection was closed.
                            Trace.WriteLine("Connection closed");
                            break;
                        }
                        catch (Exception ex)
                        {
                            // Connection failed
                            Trace.WriteLine($"Connection failed: {ex.GetType()}: {ex.Message}");
                            throw;
                        }

                        await Task.Delay(sendInterval);
                    }
                }, _sendCts.Token);
            }
        }

        public Task StopConnectionAsync()
        {
            _sendCts.Cancel();

            return _connection.StopAsync();
        }
    }
}
