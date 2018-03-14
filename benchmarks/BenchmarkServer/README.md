## Purpose

This project is to assist in Benchmarking SignalR.
It makes it easier to test local changes than having the App in the Benchmarks repo by letting us make changes in signalr branches and using the example commandline below to run the benchmarks against our branches.

The SignalRWorker that runs against this server is located at https://github.com/aspnet/benchmarks/blob/dev/src/BenchmarksWorkers/Workers/SignalRWorker.cs.

## Usage

1. Push changes you would like to test to a branch on GitHub
2. Clone aspnet/benchmarks repo to your machine or install the global BenchmarksDriver tool https://www.nuget.org/packages/BenchmarksDriver/
3. If cloned go to the BenchmarksDriver project
4. Use the following command as a guideline for running a test using your changes

`dotnet run --server <server-endpoint> --client <client-endpoint> --properties "Transport=WebSockets" --properties "HubProtocol=messagepack" -j <path to signalr.json> --repository signalr@branch-name --projectFile benchmarks/BenchmarkServer/BenchmarkServer.csproj`

5. For more info/commands see https://github.com/aspnet/benchmarks/blob/dev/src/BenchmarksDriver/README.md