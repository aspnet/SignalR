using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace CursorSample.Hubs
{
    public class BroadcastHub : Hub<IBroadcastHubClient>
    {
        private readonly ILogger<BroadcastHub> _logger;
        public BroadcastHub(ILogger<BroadcastHub> logger)
        {
            _logger = logger;
        }

        public override Task OnConnectedAsync()
        {
            _logger.LogInformation("Connection established");
            return Task.CompletedTask;
        }

        public override Task OnDisconnectedAsync(Exception ex)
        {
            _logger.LogInformation("Connection terminated");
            return Task.CompletedTask;
        }

        public async Task Broadcast(string user, string message)
        {
            await Clients.All.Receive(user, message);
        }
    }

    public interface IBroadcastHubClient
    {
        Task Receive(string user, string message);
    }
}
