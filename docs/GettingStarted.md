# Getting Started

The [aspnet/SignalR](https://github.com/aspnet/SignalR) repository is part of the larger ecosystem of [aspnet](https://github.com/aspnet) repositories. As such, it shares a somewhat unique build process that has a few dependencies you must have installed and a couple of techniques you should familiarize yourself with.

## Dependencies
In order to successfully build and test *SignalR* you'll need to ensure that your system has the following installed:
* Docker
* [NodeJS](https://nodejs.org/) version 6.9 or later
* NPM *(typically bundled with NodeJS)*

## How To Build
Full instructions for how to build repositories within the [aspnet](https://github.com/aspnet) family of repositories can be found [in  the Home repository](https://github.com/aspnet/Home/wiki/Building-from-source).

The short-hand version of that is to simply run the  `build.cmd` or `build.sh` script that's in the root of the *SignalR* repository.

The build process is handled by *KoreBuild* which can be found in the [aspnet/BuildTools](https://github.com/aspnet/BuildTools) repository, documentation for which can be found [here](https://github.com/aspnet/BuildTools/tree/dev/docs).

*KoreBuild* will do much of the heavy-lifting for you, including installing any necessary SDK versions as well as running the build and its tests.

If, after running `build.cmd` or `build.sh`, you did not get a successful build, please refer to the [Troubleshooting](#troubleshooting) section below. If your problem isn't covered, please check that you've met all of the prerequisites. If you still can't get the solution to build successfully, please open an issue so that we might assist and please consider updating this documentation to help others in the future.

## Other Resources
For more information on what's going on in the build, please see the [Build Details](BuildDetails.md) document.

For troubleshooting build or Visual Studio issues, please see the [Troubleshooting](BuildTroubleshooting.md) document.

