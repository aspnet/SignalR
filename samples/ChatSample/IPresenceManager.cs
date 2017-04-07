using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Sockets;

namespace ChatSample
{
    public interface IPresenceManager
    {
        Task<IEnumerable<UserDetails>> UsersOnline();
        Task UserJoined(Connection connection);
        Task UserLeft(Connection connection);
    }
}
