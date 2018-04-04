using System.Threading.Tasks;

namespace Microsoft.AspNetCore.SignalR.Crankier
{
    public interface IAgent
    {
        Task Pong(int id, int value);
        Task Log(int id, string text);
        Task Status(int id, StatusInformation statusInformation);
    }
}
