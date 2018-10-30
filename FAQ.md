SignalR Frequently Asked Questions
=======

This document will condense some of the more popular questions and themes that have come up in the issues we've received on SignalR. Please check the FAQ before you submit a new issue, as the issue may have already been addressed below. 

---

**What is SignalR?**

SignalR is a series of libraries for both the server and client that make it incredibly simple to add real-time web functionality to your apps. You can learn more about SignalR in the [SignalR documentation](https://docs.microsoft.com/en-us/aspnet/core/signalr/introduction). 

**How does SignalR enable "real-time HTTP?"**

There are two sets of open-source libraries that make this possible: 

1. Various client-side packages that enable the real-time functionality are available. These clients can be used with either a server-side [Hub](https://docs.microsoft.com/en-us/aspnet/core/signalr/hubs?view=aspnetcore-2.2) instance authored in .NET, or with the [SignalR Azure Service](https://azure.microsoft.com/en-us/services/signalr-service/). 

    1. [Java](https://aka.ms/signalr-client-java)
    1. [JavaScript](https://aka.ms/signalr-client-javascript)
    1. [.NET](https://aka.ms/signalr-client-dotnet)

1. Developers who are using .NET Core on the server side to build custom real-time endpoints use [SignalR Hubs](https://docs.microsoft.com/en-us/aspnet/core/signalr/hubs), which are available as a part of .NET Core (via the [ASP.NET Core NuGet](https://www.nuget.org/packages/Microsoft.AspNetCore.App)). 

1. Developers who simply need the real-time endpoint and who want to use custom client-side code to handle all events coming from the server should use of the [SignalR Azure Service](https://azure.microsoft.com/en-us/services/signalr-service/). 

**Does SignalR Reconnect Automatically?**

No. SignalR clients need to be manually reconnected. Below you will see some example JavaScript code that demonstrates one method of performing a reconnection. 

```javascript
const start = () => {
    connection.start()
        .then(() => {
            console.log('connected');
        })
        .catch(err => {
            console.error(err.toString());
            setTimeout(function () {
                start();
            }, 5000);
        });
};

connection.onclose(function () {
    start();
});

start();
```