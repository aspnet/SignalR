param([string]$Configuration = "Debug")
dotnet publish "$PSScriptRoot/.." --runtime linux-x64
docker build -t cursor-test-server "$PSScriptRoot/../bin/$Configuration/netcoreapp2.2/linux-x64/publish"
