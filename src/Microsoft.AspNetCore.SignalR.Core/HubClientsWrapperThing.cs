using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.AspNetCore.SignalR
{
    public class HubClientsWrapperThing : IHubCallerClients
    {
        private string _connectionId;
        private IHubClients _hubClients;

        public HubClientsWrapperThing(IHubClients hubClients, string ConnectionId)
        {
            _connectionId = ConnectionId;
            _hubClients = hubClients;
        }
        public IHubClients HubClients => _hubClients;

        public IClientProxy Caller => _hubClients.Client(_connectionId);

        public IClientProxy Others => _hubClients.AllExcept(new List<string> { _connectionId });

        public IClientProxy All => _hubClients.All;

        public IClientProxy AllExcept(IReadOnlyList<string> excludedIds)
        {
            return _hubClients.AllExcept(excludedIds);
        }

        public IClientProxy Client(string connectionId)
        {
            return _hubClients.Client(connectionId);
        }

        public IClientProxy Group(string groupName)
        {
            return _hubClients.Group(groupName);
        }

        public IClientProxy User(string userId)
        {
           return _hubClients.User(userId);
        }
    }
}
