# Build Details
### SDK Version
Because *SignalR* is part of the larger [aspnet](https://github.com/aspnet) ecosystem and they release together, it is not uncommon for it to target a preview version of the .NET Core SDK.

For this reason, the build downloads and installs the version of the SDK that it needs into `%USERPROFILE%/.dotnet/x64` on Windows and `$HOME/.dotnet` on Linus/macOS.

In order to make use of the SDK version it installs here, this directory must be added to your `PATH` environment variable. The build script will do this for you, but only in its own-context. That is to say, it will only be available in the PATH while the build script is running.

### Docker Usage
The build script makes use of *Docker*. In particular, it uses *Docker* to piece together testing environments including, but not limited to, launches *Redis* instances to test scale-out implementations.