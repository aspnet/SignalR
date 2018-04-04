using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.Connections;

namespace Microsoft.AspNetCore.SignalR.Crankier
{
    public class Worker : IWorker
    {
        private readonly Process _agentProcess;
        private readonly IAgent _agent;
        private readonly int _processId;
        private readonly ConcurrentBag<Client> _clients;
        private readonly CancellationTokenSource _sendStatusCts;
        private int _targetConnectionCount;

        public Worker(int agentProcessId)
        {
            _agentProcess = Process.GetProcessById(agentProcessId);
            _agent = new AgentSender(new StreamWriter(Console.OpenStandardOutput()));
            _processId = Process.GetCurrentProcess().Id;
            _clients = new ConcurrentBag<Client>();
            _sendStatusCts = new CancellationTokenSource();
        }

        public async Task Run()
        {
            _agentProcess.EnableRaisingEvents = true;
            _agentProcess.Exited += OnExited;

            Log("Worker created");

            var receiver = new WorkerReceiver(
                new StreamReader(Console.OpenStandardInput()),
                this);

            receiver.Start();

            await SendStatusUpdate(_sendStatusCts.Token);

            receiver.Stop();
        }

        public async Task Ping(int value)
        {
            Log("Worker received ping command with value {0}.", value);

            await _agent.Pong(_processId, value);
            Log("Worker sent pong command with value {0}.", value);
        }

        public async Task Connect(string targetAddress, HttpTransportType transportType, int numberOfConnections)
        {
            Log("Worker received connect command with target address {0} and number of connections {1}", targetAddress, numberOfConnections);

            _targetConnectionCount += numberOfConnections;
            for (int count = 0; count < numberOfConnections; count++)
            {
                var client = new Client();
                _clients.Add(client);

                await client.CreateAndStartConnection(targetAddress, transportType);
            }

            Log("Connections connected succesfully");
        }

        public Task StartTest(TimeSpan sendInterval, int sendBytes)
        {
            Log("Worker received start test command with interval {0} and message size {1}.", sendInterval, sendBytes);

            foreach (var client in _clients)
            {
                client.StartTest(sendBytes, sendInterval);
            }

            Log("Test started succesfully");
            return Task.CompletedTask;
        }

        public Task Stop()
        {
            Log("Worker received stop command");
            _targetConnectionCount = 0;

            while (!_clients.IsEmpty)
            {
                if (_clients.TryTake(out var client))
                {
                    client.StopConnection();
                }
            }

            _sendStatusCts.Cancel();
            Log("Connections stopped succesfully");
            _targetConnectionCount = 0;

            return Task.CompletedTask;
        }

        private void OnExited(object sender, EventArgs args)
        {
            Environment.Exit(0);
        }

        private void Log(string format, params object[] arguments)
        {
            _agent.Log(_processId, string.Format(format, arguments));
        }

        private async Task SendStatusUpdate(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                int connectedCount = 0;
                int connectingCount = 0;
                int disconnectedCount = 0;
                int reconnectingCount = 0;
                int faultedCount = 0;

                foreach (var client in _clients)
                {
                    switch (client.State)
                    {
                        case ConnectionState.Connecting:
                            connectingCount++;
                            break;
                        case ConnectionState.Connected:
                            connectedCount++;
                            break;
                        case ConnectionState.Disconnected:
                            disconnectedCount++;
                            break;
                        case ConnectionState.Reconnecting:
                            reconnectingCount++;
                            break;
                        case ConnectionState.Faulted:
                            faultedCount++;
                            break;
                    }
                }

                await _agent.Status(
                    _processId,
                    new StatusInformation
                    {
                        ConnectingCount = connectingCount,
                        ConnectedCount = connectedCount,
                        DisconnectedCount = disconnectedCount,
                        ReconnectingCount = reconnectingCount,
                        TargetConnectionCount = _targetConnectionCount,
                        FaultedCount = faultedCount,
                    }
                );

                // Sending once per 5 seconds to avoid overloading the Test Controller
                try
                {
                    await Task.Delay(5000, cancellationToken);
                }
                catch (TaskCanceledException)
                {
                }
            }
        }
    }
}
