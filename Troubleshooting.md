# Build Troubleshooting
Below are some tips for troubleshooting common issues.

### Docker-Related Errors
If you receive an error similar to:
```
C:\Program Files\Docker\Docker\Resources\bin\docker.exe: Error response from daemon: driver failed programming external connectivity on endpoint redisTestContainer (bbafe9e2b9e88b14e1d231eae755ae33755292e1399a278a846cab5fdc6ae955): Error starting userland proxy: mkdir /port/tcp:0.0.0.0:6379:tcp:172.17.0.2:6379: input/output error.
```

Or other Docker-related errors, it may be necessary to update your version of Docker. Try updating to the latest version of docker and attempting to build again.

### Loading in Visual Studio 
The command-line build makes changes to the file system that will sometimes prevent you from successfully loading all of the projects in the solution when you open the solution in Visual Studio.

Typically, this happens to the sample web applications and presents itsself with the error `The SDK 'Microsoft.NET.Sdk.Web'specified could not be found.`.

This is because the command-line build has created a `global.json` file in the root of the repository that pins the SDK version to one that is not installed on your system. Fortunately, the build script installed it for you (see the [SDK Version](#sdk-version) section above).

You can run the `launch-vs.cmd` from the *Visual Studio Developer Command Prompt* to set the appropriate `PATH` variable and launch Visual Studio on your behalf.

If you'd like to be able to skip this step, you can follow the recommendations from [the Home repository](https://github.com/aspnet/Home/wiki/Building-from-source) and permanently modify your `PATH` environment variable to include the `.dotnet/x64` folder created by the build script.