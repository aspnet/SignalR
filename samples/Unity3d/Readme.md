SignalR Sample for Windows Mixed Reality
========================================

Using SignalR over the native uNet integration makes for a more seamless
connectivity experience in enterprise environments, as all networking is done
using standard web technologies, that are less likely to be blocked by
firewalls. It is also easier to connect endpoints built with technolgy stacks
other than Unity.

This is a sample ASP.Net Core SignalR server and client. The client is a Unity
project that has been tested on UWP and standalone Windows. By enabling VR
support in Unity, you can use this for a HoloLens or Windows Mixed Reality app.

Tool versions
-------------

-   Unity 2018.2.3f1. Prior 2018 versions of Unity have shown bugs with network
    connectivity and the IL2CPP backend. For 2017 and prior, see [Compatibility
    between ASP.Net Core SignalR and ASP.Net
    4.6](#compatibility-between-asp.net-core-signalr-and-asp.net-4.6)

-   UWP SDK 10.0.17134. Should work with older versions.

-   Visual Studio 2017 Version 15.8

Running the sample
------------------

### Server

To run the sample, open the ASP.Net web site in Visual Studio, and run it by
pressing f5. This will start a local instance of your server and open the
index.html page in your browser. The page has a text box for a username and the
message. You can type anything in these boxes. Pressing the send button will
send the message to all connected clients. The browser will receive this message
and display it. If you can see the message, your server is working. This url
needs to be accessible from all client devices you intend to use for testing. If
it is not, you can easily publish this server to an Azure App Service.

To do this, right click on the project file, and click the publish button in the
context menu. Follow the steps in the wizard to complete your deployment. When
publishing to an Azure Web app, you will need to use the platform settings in
Azure for your new web app to enable web sockets, which is disabled by default.
This is not required for your sample to work, as SignalR supports fallbacks, but
it will give you a better quality, lower latency connection between your clients
and the server.

### Client

Open the client app folder in Unity Editor. Once the editor finished importing
the asset library, open the SampleScene unity scene in the scenes asset folder.
It should contain a camera, directional light and a text gameobject with an
attached ChatClientBehaviour. This behavior has a field called SignalRServer
which lets you specify the URL for your server. A default fallback is provided
in the code, but this server is not guaranteed to be available.

When you run the app, the text should change to indicate connection changes.
Once it is connected, you will see a message in the game window stating that
your device is connected. You will see the same message in the page you earlier
opened in your browser. Sending a message in the web page should now change the
text displayed in the game window, as will connecting other clients.

You can open the Build Settings, change the selected platform to UWP and build a
player. Once the output is built, you can open that solution in Visual Studio
and deploy it to your local machine. You may run into a deployment issue with
the splash screen image. One solution is to remove the scaling identifier from
one of the splash screen images and updating the manifest.

### Player Settings

You will need to use the IL2CPP backend, and .net 4.6 API compatibility. There
is also a link.xml file included in the Assets folder that excludes the SignalR
binaries from the assembly stripping process. This is important because they use
dependency injection at runtime. Required parts of the binaries may get stripped
if you do not include this file, resulting in strange runtime errors in your
player.

Updating the client libraries
-----------------------------

The client has a utilities folder in which you’ll find a PowerShell script. This
script will download nuget and use it to get the latest version of the client
libraries. It will then extract the appropriate DLL’s from the nuget package and
copy them to the plugins folder in the unity project. If they’re already there,
you may find that they get copied to a child folder, so it’s a good idea to
delete the SignalR folder from the plugins folder first.

Version conflicts with JSON.Net
-------------------------------

If you use the Mixed Reality Toolkit, for example, you may find that it already
contains a version of Json.Net compiled for .Net 3.5. You may have to recompile
your SignalR project against this version of the Json.Net dll to fix
incompatibilities. Refer to the documentation for the SignalR project on how to
do this.

Compatibility between ASP.Net Core SignalR and ASP.Net 4.6
----------------------------------------------------------

ASP.Net Core SignalR Client libraries are not compatible with ASP.Net 4.6
server, and vice versa. The protocol used to connect between the client and
server was changed. The client libraries you use, must match the server
implementation. If they do not, you may encounter an http 404 error when trying
to establish a connection.

Unity 2017 and earlier do not support .Net Standard 2.0 plugins. The .Net Core
client libraries are only distributed as .Net Standard 2.0 libraries. If you
need to use an earlier version of Unity, you will need to use the .Net Framework
version of ASP.Net SignalR, for which there are .Net 4.6 compatible libraries.
The process of importing and using the dll’s are the same as demonstrated in
this sample. Additionally, you can use the .Net backend for UWP apps.
