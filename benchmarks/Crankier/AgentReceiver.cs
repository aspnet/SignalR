using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Microsoft.AspNetCore.SignalR.Crankier
{
    public class AgentReceiver
    {
        private readonly StreamReader _reader;
        private readonly IAgent _agent;

        public AgentReceiver(StreamReader reader, IAgent agent)
        {
            _reader = reader;
            _agent = agent;
        }

        public void Start()
        {
            Task.Run(async () =>
            {
                var messageString = await _reader.ReadLineAsync();
                while (messageString != null)
                {
                    try
                    {
                        var message = JsonConvert.DeserializeObject<Message>(messageString);

                        switch (message.Command.ToLowerInvariant())
                        {
                            case "pong":
                                await _agent.Pong(
                                    message.Value["Id"].ToObject<int>(),
                                    message.Value["Value"].ToObject<int>());
                                break;
                            case "log":
                                await _agent.Log(
                                    message.Value["Id"].ToObject<int>(),
                                    message.Value["Text"].ToObject<string>());
                                break;
                            case "status":
                                await _agent.Status(
                                    message.Value["Id"].ToObject<int>(),
                                    message.Value["StatusInformation"].ToObject<StatusInformation>());
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine($"Error parsing '{messageString}': {ex.Message}");
                    }

                    messageString = await _reader.ReadLineAsync();
                }
            });
        }
    }
}
