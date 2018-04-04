using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.Connections;

namespace Microsoft.AspNetCore.SignalR.Crankier
{
    public interface IWorker
    {
        Task Ping(int value);
        Task Connect(string targetAddress, HttpTransportType transportType, int numberOfConnections);
        Task StartTest(TimeSpan sendInterval, int sendBytes);
        Task Stop();
    }
}
