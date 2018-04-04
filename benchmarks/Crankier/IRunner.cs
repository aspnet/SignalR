using System.Threading.Tasks;

namespace Microsoft.AspNetCore.SignalR.Crankier
{
    public interface IRunner
    {
        Task PongWorker(int workerId, int value);

        Task LogAgent(string format, params object[] arguments);

        Task LogWorker(int workerId, string format, params object[] arguments);
    }
}
