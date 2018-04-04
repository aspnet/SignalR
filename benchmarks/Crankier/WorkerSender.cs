using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.Connections;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.AspNetCore.SignalR.Crankier
{
    public class WorkerSender : IWorker
    {
        private readonly StreamWriter _outputStreamWriter;

        public WorkerSender(StreamWriter outputStreamWriter)
        {
            _outputStreamWriter = outputStreamWriter;
        }

        public async Task Ping(int value)
        {
            await Send("ping", JToken.FromObject(
                new
                {
                    Value = value
                }));
        }

        public async Task Connect(string targetAddress, HttpTransportType transportType, int numberOfConnections)
        {
            await Send("connect", JToken.FromObject(
                new
                {
                    TargetAddress = targetAddress,
                    TransportType = transportType,
                    NumberOfConnections = numberOfConnections
                }));
        }

        public async Task StartTest(TimeSpan sendInterval, int sendBytes)
        {
            var parameters = new
            {
                SendInterval = sendInterval.TotalMilliseconds,
                SendBytes = sendBytes
            };

            await Send("starttest", JToken.FromObject(parameters));
        }

        public async Task Stop()
        {
            await Send("stop", null);
        }

        private async Task Send(string method, JToken parameters)
        {
            await _outputStreamWriter.WriteLineAsync(
                JsonConvert.SerializeObject(new Message()
                {
                    Command = method,
                    Value = parameters
                }));

            await _outputStreamWriter.FlushAsync();
        }
    }
}
