// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Channels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Microsoft.AspNetCore.SignalR.Tests.Common;
using Microsoft.AspNetCore.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Xunit;
using Microsoft.AspNetCore.Hosting;

namespace Microsoft.AspNetCore.SignalR.Tests
{
    public class HubEndpointTests
    {
        [Fact]
        public async Task HubsAreDisposed()
        {
            var trackDispose = new TrackDispose();
            var serviceProvider = CreateServiceProvider(s => s.AddSingleton(trackDispose));
            var endPoint = serviceProvider.GetService<HubEndPoint<DisposeTrackingHub>>();

            using (var client = new TestClient())
            {
                var endPointTask = endPoint.OnConnectedAsync(client.Connection);

                // kill the connection
                client.Dispose();

                await endPointTask;

                Assert.Equal(2, trackDispose.DisposeCount);
            }
        }

        [Fact]
        public async Task MissingNegotiateAndMessageSentFromHubConnectionCanBeDisposedCleanly()
        {
            var serviceProvider = CreateServiceProvider();
            var endPoint = serviceProvider.GetService<HubEndPoint<SimpleHub>>();

            using (var client = new TestClient())
            {
                // TestClient automatically writes negotiate, for this test we want to assume negotiate never gets sent
                client.Connection.Transport.In.TryRead(out var item);

                var endPointTask = endPoint.OnConnectedAsync(client.Connection);

                // kill the connection
                client.Dispose();

                await endPointTask;
            }
        }

        [Fact]
        public async Task NegotiateTimesOut()
        {
            var serviceProvider = CreateServiceProvider();
            var endPoint = serviceProvider.GetService<HubEndPoint<SimpleHub>>();

            using (var client = new TestClient())
            {
                // TestClient automatically writes negotiate, for this test we want to assume negotiate never gets sent
                client.Connection.Transport.In.TryRead(out var item);

                await endPoint.OnConnectedAsync(client.Connection).OrTimeout(TimeSpan.FromSeconds(10));
            }
        }

        [Fact]
        public async Task CanLoadHubContext()
        {
            var serviceProvider = CreateServiceProvider();
            var context = serviceProvider.GetRequiredService<IHubContext<SimpleHub>>();
            await context.Clients.All.InvokeAsync("Send", "test");
        }

        [Fact]
        public async Task CanLoadTypedHubContext()
        {
            var serviceProvider = CreateServiceProvider();
            var context = serviceProvider.GetRequiredService<IHubContext<SimpleTypedHub, ITypedHubClient>>();
            await context.Clients.All.Send("test");
        }

        [Fact]
        public async Task LifetimeManagerOnDisconnectedAsyncCalledIfLifetimeManagerOnConnectedAsyncThrows()
        {
            var mockLifetimeManager = new Mock<HubLifetimeManager<Hub>>();
            mockLifetimeManager
                .Setup(m => m.OnConnectedAsync(It.IsAny<HubConnectionContext>()))
                .Throws(new InvalidOperationException("Lifetime manager OnConnectedAsync failed."));
            var mockHubActivator = new Mock<IHubActivator<Hub>>();

            var serviceProvider = CreateServiceProvider(services =>
            {
                services.AddSingleton(mockLifetimeManager.Object);
                services.AddSingleton(mockHubActivator.Object);
            });

            var endPoint = serviceProvider.GetService<HubEndPoint<Hub>>();

            using (var client = new TestClient())
            {
                var exception =
                    await Assert.ThrowsAsync<InvalidOperationException>(
                        async () => await endPoint.OnConnectedAsync(client.Connection));
                Assert.Equal("Lifetime manager OnConnectedAsync failed.", exception.Message);

                client.Dispose();

                mockLifetimeManager.Verify(m => m.OnConnectedAsync(It.IsAny<HubConnectionContext>()), Times.Once);
                mockLifetimeManager.Verify(m => m.OnDisconnectedAsync(It.IsAny<HubConnectionContext>()), Times.Once);
                // No hubs should be created since the connection is terminated
                mockHubActivator.Verify(m => m.Create(), Times.Never);
                mockHubActivator.Verify(m => m.Release(It.IsAny<Hub>()), Times.Never);
            }
        }

        [Fact]
        public async Task HubOnDisconnectedAsyncCalledIfHubOnConnectedAsyncThrows()
        {
            var mockLifetimeManager = new Mock<HubLifetimeManager<OnConnectedThrowsHub>>();
            var serviceProvider = CreateServiceProvider(services =>
            {
                services.AddSingleton(mockLifetimeManager.Object);
            });

            var endPoint = serviceProvider.GetService<HubEndPoint<OnConnectedThrowsHub>>();

            using (var client = new TestClient())
            {
                var endPointTask = endPoint.OnConnectedAsync(client.Connection);
                client.Dispose();

                var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => await endPointTask);
                Assert.Equal("Hub OnConnected failed.", exception.Message);

                mockLifetimeManager.Verify(m => m.OnConnectedAsync(It.IsAny<HubConnectionContext>()), Times.Once);
                mockLifetimeManager.Verify(m => m.OnDisconnectedAsync(It.IsAny<HubConnectionContext>()), Times.Once);
            }
        }

        [Fact]
        public async Task LifetimeManagerOnDisconnectedAsyncCalledIfHubOnDisconnectedAsyncThrows()
        {
            var mockLifetimeManager = new Mock<HubLifetimeManager<OnDisconnectedThrowsHub>>();
            var serviceProvider = CreateServiceProvider(services =>
            {
                services.AddSingleton(mockLifetimeManager.Object);
            });

            var endPoint = serviceProvider.GetService<HubEndPoint<OnDisconnectedThrowsHub>>();

            using (var client = new TestClient())
            {
                var endPointTask = endPoint.OnConnectedAsync(client.Connection);
                client.Dispose();

                var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => await endPointTask);
                Assert.Equal("Hub OnDisconnected failed.", exception.Message);

                mockLifetimeManager.Verify(m => m.OnConnectedAsync(It.IsAny<HubConnectionContext>()), Times.Once);
                mockLifetimeManager.Verify(m => m.OnDisconnectedAsync(It.IsAny<HubConnectionContext>()), Times.Once);
            }
        }

        [Fact]
        public async Task HubMethodCanReturnValueFromTask()
        {
            var serviceProvider = CreateServiceProvider();

            var endPoint = serviceProvider.GetService<HubEndPoint<MethodHub>>();

            using (var client = new TestClient())
            {
                var endPointTask = endPoint.OnConnectedAsync(client.Connection);

                var result = (await client.InvokeAsync(nameof(MethodHub.TaskValueMethod)).OrTimeout()).Result;

                // json serializer makes this a long
                Assert.Equal(42L, result);

                // kill the connection
                client.Dispose();

                await endPointTask.OrTimeout();
            }
        }

        [Theory]
        [MemberData(nameof(HubTypes))]
        public async Task HubMethodsAreCaseInsensitive(Type hubType)
        {
            var serviceProvider = CreateServiceProvider();

            dynamic endPoint = serviceProvider.GetService(GetEndPointType(hubType));

            using (var client = new TestClient())
            {
                Task endPointTask = endPoint.OnConnectedAsync(client.Connection);

                var result = (await client.InvokeAsync("echo", "hello").OrTimeout()).Result;

                Assert.Equal("hello", result);

                // kill the connection
                client.Dispose();

                await endPointTask.OrTimeout();
            }
        }

        [Theory]
        [InlineData(nameof(MethodHub.MethodThatThrows))]
        [InlineData(nameof(MethodHub.MethodThatYieldsFailedTask))]
        public async Task HubMethodCanThrowOrYieldFailedTask(string methodName)
        {
            var serviceProvider = CreateServiceProvider();

            var endPoint = serviceProvider.GetService<HubEndPoint<MethodHub>>();

            using (var client = new TestClient())
            {
                var endPointTask = endPoint.OnConnectedAsync(client.Connection);

                var result = (await client.InvokeAsync(methodName).OrTimeout());

                Assert.Equal("BOOM!", result.Error);

                // kill the connection
                client.Dispose();

                await endPointTask.OrTimeout();
            }
        }

        [Fact]
        public async Task HubMethodCanReturnValue()
        {
            var serviceProvider = CreateServiceProvider();

            var endPoint = serviceProvider.GetService<HubEndPoint<MethodHub>>();

            using (var client = new TestClient())
            {
                var endPointTask = endPoint.OnConnectedAsync(client.Connection);

                var result = (await client.InvokeAsync(nameof(MethodHub.ValueMethod)).OrTimeout()).Result;

                // json serializer makes this a long
                Assert.Equal(43L, result);

                // kill the connection
                client.Dispose();

                await endPointTask.OrTimeout();
            }
        }

        [Fact]
        public async Task HubMethodCanBeVoid()
        {
            var serviceProvider = CreateServiceProvider();

            var endPoint = serviceProvider.GetService<HubEndPoint<MethodHub>>();

            using (var client = new TestClient())
            {
                var endPointTask = endPoint.OnConnectedAsync(client.Connection);

                var result = (await client.InvokeAsync(nameof(MethodHub.VoidMethod)).OrTimeout()).Result;

                Assert.Null(result);

                // kill the connection
                client.Dispose();

                await endPointTask.OrTimeout();
            }
        }

        [Theory]
        [InlineData(nameof(MethodHub.VoidMethod))]
        [InlineData(nameof(MethodHub.MethodThatThrows))]
        public async Task NonBlockingInvocationDoesNotSendCompletion(string methodName)
        {
            var serviceProvider = CreateServiceProvider();

            var endPoint = serviceProvider.GetService<HubEndPoint<MethodHub>>();

            using (var client = new TestClient(synchronousCallbacks: true))
            {
                var endPointTask = endPoint.OnConnectedAsync(client.Connection);

                // This invocation should be completely synchronous
                await client.SendInvocationAsync(methodName, nonBlocking: true).OrTimeout();

                // Nothing should have been written
                Assert.False(client.Application.In.TryRead(out var buffer));

                // kill the connection
                client.Dispose();

                await endPointTask.OrTimeout();
            }
        }

        [Fact]
        public async Task HubMethodWithMultiParam()
        {
            var serviceProvider = CreateServiceProvider();

            var endPoint = serviceProvider.GetService<HubEndPoint<MethodHub>>();

            using (var client = new TestClient())
            {
                var endPointTask = endPoint.OnConnectedAsync(client.Connection);

                var result = (await client.InvokeAsync(nameof(MethodHub.ConcatString), (byte)32, 42, 'm', "string").OrTimeout()).Result;

                Assert.Equal("32, 42, m, string", result);

                // kill the connection
                client.Dispose();

                await endPointTask.OrTimeout();
            }
        }

        [Fact]
        public async Task CanCallInheritedHubMethodFromInheritingHub()
        {
            var serviceProvider = CreateServiceProvider();

            var endPoint = serviceProvider.GetService<HubEndPoint<InheritedHub>>();

            using (var client = new TestClient())
            {
                var endPointTask = endPoint.OnConnectedAsync(client.Connection);

                var result = (await client.InvokeAsync(nameof(InheritedHub.BaseMethod), "string").OrTimeout()).Result;

                Assert.Equal("string", result);

                // kill the connection
                client.Dispose();

                await endPointTask.OrTimeout();
            }
        }

        [Fact]
        public async Task CanCallOverridenVirtualHubMethod()
        {
            var serviceProvider = CreateServiceProvider();

            var endPoint = serviceProvider.GetService<HubEndPoint<InheritedHub>>();

            using (var client = new TestClient())
            {
                var endPointTask = endPoint.OnConnectedAsync(client.Connection);

                var result = (await client.InvokeAsync(nameof(InheritedHub.VirtualMethod), 10).OrTimeout()).Result;

                Assert.Equal(0L, result);

                // kill the connection
                client.Dispose();

                await endPointTask.OrTimeout();
            }
        }

        [Fact]
        public async Task CannotCallOverriddenBaseHubMethod()
        {
            var serviceProvider = CreateServiceProvider();

            var endPoint = serviceProvider.GetService<HubEndPoint<MethodHub>>();

            using (var client = new TestClient())
            {
                var endPointTask = endPoint.OnConnectedAsync(client.Connection);

                var result = await client.InvokeAsync(nameof(MethodHub.OnDisconnectedAsync)).OrTimeout();

                Assert.Equal("Unknown hub method 'OnDisconnectedAsync'", result.Error);

                // kill the connection
                client.Dispose();

                await endPointTask.OrTimeout();
            }
        }

        [Fact]
        public void HubsCannotHaveOverloadedMethods()
        {
            var serviceProvider = CreateServiceProvider();

            try
            {
                var endPoint = serviceProvider.GetService<HubEndPoint<InvalidHub>>();
                Assert.True(false);
            }
            catch (NotSupportedException ex)
            {
                Assert.Equal("Duplicate definitions of 'OverloadedMethod'. Overloading is not supported.", ex.Message);
            }
        }

        [Fact]
        public async Task CannotCallStaticHubMethods()
        {
            var serviceProvider = CreateServiceProvider();

            var endPoint = serviceProvider.GetService<HubEndPoint<MethodHub>>();

            using (var client = new TestClient())
            {
                var endPointTask = endPoint.OnConnectedAsync(client.Connection);

                var result = await client.InvokeAsync(nameof(MethodHub.StaticMethod)).OrTimeout();

                Assert.Equal("Unknown hub method 'StaticMethod'", result.Error);

                // kill the connection
                client.Dispose();

                await endPointTask.OrTimeout();
            }
        }

        [Fact]
        public async Task CannotCallObjectMethodsOnHub()
        {
            var serviceProvider = CreateServiceProvider();

            var endPoint = serviceProvider.GetService<HubEndPoint<MethodHub>>();

            using (var client = new TestClient())
            {
                var endPointTask = endPoint.OnConnectedAsync(client.Connection);

                var result = await client.InvokeAsync(nameof(MethodHub.ToString)).OrTimeout();
                Assert.Equal("Unknown hub method 'ToString'", result.Error);

                result = await client.InvokeAsync(nameof(MethodHub.GetHashCode)).OrTimeout();
                Assert.Equal("Unknown hub method 'GetHashCode'", result.Error);

                result = await client.InvokeAsync(nameof(MethodHub.Equals)).OrTimeout();
                Assert.Equal("Unknown hub method 'Equals'", result.Error);

                result = await client.InvokeAsync(nameof(MethodHub.ReferenceEquals)).OrTimeout();
                Assert.Equal("Unknown hub method 'ReferenceEquals'", result.Error);

                // kill the connection
                client.Dispose();

                await endPointTask.OrTimeout();
            }
        }

        [Fact]
        public async Task CannotCallDisposeMethodOnHub()
        {
            var serviceProvider = CreateServiceProvider();

            var endPoint = serviceProvider.GetService<HubEndPoint<MethodHub>>();

            using (var client = new TestClient())
            {
                var endPointTask = endPoint.OnConnectedAsync(client.Connection);

                var result = await client.InvokeAsync(nameof(MethodHub.Dispose)).OrTimeout();

                Assert.Equal("Unknown hub method 'Dispose'", result.Error);

                // kill the connection
                client.Dispose();

                await endPointTask.OrTimeout();
            }
        }

        [Theory]
        [MemberData(nameof(HubTypes))]
        public async Task BroadcastHubMethodSendsToAllClients(Type hubType)
        {
            var serviceProvider = CreateServiceProvider();

            dynamic endPoint = serviceProvider.GetService(GetEndPointType(hubType));

            using (var firstClient = new TestClient())
            using (var secondClient = new TestClient())
            {
                Task firstEndPointTask = endPoint.OnConnectedAsync(firstClient.Connection);
                Task secondEndPointTask = endPoint.OnConnectedAsync(secondClient.Connection);

                await Task.WhenAll(firstClient.Connected, secondClient.Connected).OrTimeout();

                await firstClient.SendInvocationAsync(nameof(MethodHub.BroadcastMethod), "test").OrTimeout();

                foreach (var result in await Task.WhenAll(
                    firstClient.ReadAsync(),
                    secondClient.ReadAsync()).OrTimeout())
                {
                    var invocation = Assert.IsType<InvocationMessage>(result);
                    Assert.Equal("Broadcast", invocation.Target);
                    Assert.Single(invocation.Arguments);
                    Assert.Equal("test", invocation.Arguments[0]);
                }

                // kill the connections
                firstClient.Dispose();
                secondClient.Dispose();

                await Task.WhenAll(firstEndPointTask, secondEndPointTask).OrTimeout();
            }
        }

        [Theory]
        [MemberData(nameof(HubTypes))]
        public async Task SendToAllExcept(Type hubType)
        {
            var serviceProvider = CreateServiceProvider();

            dynamic endPoint = serviceProvider.GetService(GetEndPointType(hubType));

            using (var firstClient = new TestClient())
            using (var secondClient = new TestClient())
            using (var thirdClient = new TestClient())
            {
                Task firstEndPointTask = endPoint.OnConnectedAsync(firstClient.Connection);
                Task secondEndPointTask = endPoint.OnConnectedAsync(secondClient.Connection);
                Task thirdEndPointTask = endPoint.OnConnectedAsync(thirdClient.Connection);

                await Task.WhenAll(firstClient.Connected, secondClient.Connected, thirdClient.Connected).OrTimeout();

                var excludeSecondClientId = new HashSet<string>();
                excludeSecondClientId.Add(secondClient.Connection.ConnectionId);
                var excludeThirdClientId =  new HashSet<string>();
                excludeThirdClientId.Add(thirdClient.Connection.ConnectionId);

                await firstClient.SendInvocationAsync("SendToAllExcept", "To second", excludeThirdClientId).OrTimeout();
                await firstClient.SendInvocationAsync("SendToAllExcept", "To third", excludeSecondClientId).OrTimeout();

                var secondClientResult = await secondClient.ReadAsync().OrTimeout();
                var invocation = Assert.IsType<InvocationMessage>(secondClientResult);
                Assert.Equal("Send", invocation.Target);
                Assert.Equal("To second", invocation.Arguments[0]);

                var thirdClientResult = await thirdClient.ReadAsync().OrTimeout();
                invocation = Assert.IsType<InvocationMessage>(thirdClientResult);
                Assert.Equal("Send", invocation.Target);
                Assert.Equal("To third", invocation.Arguments[0]);

                // kill the connections
                firstClient.Dispose();
                secondClient.Dispose();
                thirdClient.Dispose();

                await Task.WhenAll(firstEndPointTask, secondEndPointTask, thirdEndPointTask).OrTimeout();
            }
        }

        [Theory]
        [MemberData(nameof(HubTypes))]
        public async Task HubsCanAddAndSendToGroup(Type hubType)
        {
            var serviceProvider = CreateServiceProvider();

            dynamic endPoint = serviceProvider.GetService(GetEndPointType(hubType));

            using (var firstClient = new TestClient())
            using (var secondClient = new TestClient())
            {
                Task firstEndPointTask = endPoint.OnConnectedAsync(firstClient.Connection);
                Task secondEndPointTask = endPoint.OnConnectedAsync(secondClient.Connection);

                await Task.WhenAll(firstClient.Connected, secondClient.Connected).OrTimeout();

                var result = (await firstClient.InvokeAsync("GroupSendMethod", "testGroup", "test").OrTimeout()).Result;

                // check that 'firstConnection' hasn't received the group send
                Assert.Null(firstClient.TryRead());

                // check that 'secondConnection' hasn't received the group send
                Assert.Null(secondClient.TryRead());

                result = (await secondClient.InvokeAsync(nameof(MethodHub.GroupAddMethod), "testGroup").OrTimeout()).Result;

                await firstClient.SendInvocationAsync(nameof(MethodHub.GroupSendMethod), "testGroup", "test").OrTimeout();

                // check that 'secondConnection' has received the group send
                var hubMessage = await secondClient.ReadAsync().OrTimeout();
                var invocation = Assert.IsType<InvocationMessage>(hubMessage);
                Assert.Equal("Send", invocation.Target);
                Assert.Single(invocation.Arguments);
                Assert.Equal("test", invocation.Arguments[0]);

                // kill the connections
                firstClient.Dispose();
                secondClient.Dispose();

                await Task.WhenAll(firstEndPointTask, secondEndPointTask).OrTimeout();
            }
        }

        [Fact]
        public async Task RemoveFromGroupWhenNotInGroupDoesNotFail()
        {
            var serviceProvider = CreateServiceProvider();

            var endPoint = serviceProvider.GetService<HubEndPoint<MethodHub>>();

            using (var client = new TestClient())
            {
                var endPointTask = endPoint.OnConnectedAsync(client.Connection);

                await client.SendInvocationAsync(nameof(MethodHub.GroupRemoveMethod), "testGroup").OrTimeout();

                // kill the connection
                client.Dispose();

                await endPointTask.OrTimeout();
            }
        }

        [Theory]
        [MemberData(nameof(HubTypes))]
        public async Task HubsCanSendToUser(Type hubType)
        {
            var serviceProvider = CreateServiceProvider();

            dynamic endPoint = serviceProvider.GetService(GetEndPointType(hubType));

            using (var firstClient = new TestClient())
            using (var secondClient = new TestClient())
            {
                Task firstEndPointTask = endPoint.OnConnectedAsync(firstClient.Connection);
                Task secondEndPointTask = endPoint.OnConnectedAsync(secondClient.Connection);

                await Task.WhenAll(firstClient.Connected, secondClient.Connected).OrTimeout();

                await firstClient.SendInvocationAsync("ClientSendMethod", secondClient.Connection.User.Identity.Name, "test").OrTimeout();

                // check that 'secondConnection' has received the group send
                var hubMessage = await secondClient.ReadAsync().OrTimeout();
                var invocation = Assert.IsType<InvocationMessage>(hubMessage);
                Assert.Equal("Send", invocation.Target);
                Assert.Single(invocation.Arguments);
                Assert.Equal("test", invocation.Arguments[0]);

                // kill the connections
                firstClient.Dispose();
                secondClient.Dispose();

                await Task.WhenAll(firstEndPointTask, secondEndPointTask).OrTimeout();
            }
        }

        [Theory]
        [MemberData(nameof(HubTypes))]
        public async Task HubsCanSendToConnection(Type hubType)
        {
            var serviceProvider = CreateServiceProvider();

            dynamic endPoint = serviceProvider.GetService(GetEndPointType(hubType));

            using (var firstClient = new TestClient())
            using (var secondClient = new TestClient())
            {
                Task firstEndPointTask = endPoint.OnConnectedAsync(firstClient.Connection);
                Task secondEndPointTask = endPoint.OnConnectedAsync(secondClient.Connection);

                await Task.WhenAll(firstClient.Connected, secondClient.Connected).OrTimeout();

                await firstClient.SendInvocationAsync("ConnectionSendMethod", secondClient.Connection.ConnectionId, "test").OrTimeout();

                // check that 'secondConnection' has received the group send
                var hubMessage = await secondClient.ReadAsync().OrTimeout();
                var invocation = Assert.IsType<InvocationMessage>(hubMessage);
                Assert.Equal("Send", invocation.Target);
                Assert.Single(invocation.Arguments);
                Assert.Equal("test", invocation.Arguments[0]);

                // kill the connections
                firstClient.Dispose();
                secondClient.Dispose();

                await Task.WhenAll(firstEndPointTask, secondEndPointTask).OrTimeout();
            }
        }

        [Fact]
        public async Task DelayedSendTest()
        {
            var serviceProvider = CreateServiceProvider();

            dynamic endPoint = serviceProvider.GetService(GetEndPointType(typeof(HubT)));

            using (var firstClient = new TestClient())
            using (var secondClient = new TestClient())
            {
                Task firstEndPointTask = endPoint.OnConnectedAsync(firstClient.Connection);
                Task secondEndPointTask = endPoint.OnConnectedAsync(secondClient.Connection);

                await Task.WhenAll(firstClient.Connected, secondClient.Connected).OrTimeout();

                await firstClient.SendInvocationAsync("DelayedSend", secondClient.Connection.ConnectionId, "test").OrTimeout();

                // check that 'secondConnection' has received the group send
                var hubMessage = await secondClient.ReadAsync().OrTimeout();
                var invocation = Assert.IsType<InvocationMessage>(hubMessage);
                Assert.Equal("Send", invocation.Target);
                Assert.Single(invocation.Arguments);
                Assert.Equal("test", invocation.Arguments[0]);

                // kill the connections
                firstClient.Dispose();
                secondClient.Dispose();

                await Task.WhenAll(firstEndPointTask, secondEndPointTask).OrTimeout();
            }
        }

        [Theory]
        [InlineData(nameof(StreamingHub.CounterChannel))]
        [InlineData(nameof(StreamingHub.CounterObservable))]
        public async Task HubsCanStreamResponses(string method)
        {
            var serviceProvider = CreateServiceProvider();

            var endPoint = serviceProvider.GetService<HubEndPoint<StreamingHub>>();

            using (var client = new TestClient())
            {
                var endPointLifetime = endPoint.OnConnectedAsync(client.Connection);

                await client.Connected.OrTimeout();

                var messages = await client.StreamAsync(method, 4).OrTimeout();

                Assert.Equal(5, messages.Count);
                AssertHubMessage(new StreamItemMessage(string.Empty, "0"), messages[0]);
                AssertHubMessage(new StreamItemMessage(string.Empty, "1"), messages[1]);
                AssertHubMessage(new StreamItemMessage(string.Empty, "2"), messages[2]);
                AssertHubMessage(new StreamItemMessage(string.Empty, "3"), messages[3]);
                AssertHubMessage(new CompletionMessage(string.Empty, error: null, result: null, hasResult: false), messages[4]);

                client.Dispose();

                await endPointLifetime.OrTimeout();
            }
        }

        [Fact]
        public async Task UnauthorizedConnectionCannotInvokeHubMethodWithAuthorization()
        {
            var serviceProvider = CreateServiceProvider(services =>
            {
                services.AddAuthorization(options =>
                {
                    options.AddPolicy("test", policy =>
                    {
                        policy.RequireClaim(ClaimTypes.NameIdentifier);
                        policy.AddAuthenticationSchemes("Default");
                    });
                });
            });

            var endPoint = serviceProvider.GetService<HubEndPoint<MethodHub>>();

            using (var client = new TestClient())
            {
                var endPointLifetime = endPoint.OnConnectedAsync(client.Connection);

                await client.Connected.OrTimeout();

                var message = await client.InvokeAsync(nameof(MethodHub.AuthMethod)).OrTimeout();

                Assert.NotNull(message.Error);

                client.Dispose();

                await endPointLifetime.OrTimeout();
            }
        }

        [Fact]
        public async Task AuthorizedConnectionCanInvokeHubMethodWithAuthorization()
        {
            var serviceProvider = CreateServiceProvider(services =>
            {
                services.AddAuthorization(options =>
                {
                    options.AddPolicy("test", policy =>
                    {
                        policy.RequireClaim(ClaimTypes.NameIdentifier);
                        policy.AddAuthenticationSchemes("Default");
                    });
                });
            });

            var endPoint = serviceProvider.GetService<HubEndPoint<MethodHub>>();

            using (var client = new TestClient())
            {
                client.Connection.User.AddIdentity(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "name") }));
                var endPointLifetime = endPoint.OnConnectedAsync(client.Connection);

                await client.Connected.OrTimeout();

                var message = await client.InvokeAsync(nameof(MethodHub.AuthMethod)).OrTimeout();

                Assert.Null(message.Error);

                client.Dispose();

                await endPointLifetime.OrTimeout();
            }
        }

        [Fact]
        public async Task HubOptionsCanUseCustomJsonSerializerSettings()
        {
            var serviceProvider = CreateServiceProvider(services =>
            {
                services.AddSignalR(o =>
                {
                    o.JsonSerializerSettings = new JsonSerializerSettings
                    {
                        ContractResolver = new CamelCasePropertyNamesContractResolver()
                    };
                });
            });

            var endPoint = serviceProvider.GetService<HubEndPoint<MethodHub>>();

            using (var client = new TestClient())
            {
                var endPointLifetime = endPoint.OnConnectedAsync(client.Connection);

                await client.Connected.OrTimeout();

                await client.SendInvocationAsync(nameof(MethodHub.BroadcastItem)).OrTimeout();

                var message = await client.ReadAsync().OrTimeout() as InvocationMessage;

                var customItem = message.Arguments[0].ToString();
                // Originally "Message" and "paramName"
                Assert.Contains("message", customItem);
                Assert.Contains("paramName", customItem);

                client.Dispose();

                await endPointLifetime.OrTimeout();
            }
        }

        [Fact]
        public async Task CanGetHttpContextFromHubConnectionContext()
        {
            var serviceProvider = CreateServiceProvider();

            var endPoint = serviceProvider.GetService<HubEndPoint<MethodHub>>();

            using (var client = new TestClient())
            {
                var httpContext = new DefaultHttpContext();
                client.Connection.SetHttpContext(httpContext);
                var endPointLifetime = endPoint.OnConnectedAsync(client.Connection);

                await client.Connected.OrTimeout();

                var result = (await client.InvokeAsync(nameof(MethodHub.HasHttpContext)).OrTimeout()).Result;
                Assert.True((bool)result);

                client.Dispose();

                await endPointLifetime.OrTimeout();
            }
        }

        [Fact]
        public async Task GetHttpContextFromHubConnectionContextHandlesNull()
        {
            var serviceProvider = CreateServiceProvider();

            var endPoint = serviceProvider.GetService<HubEndPoint<MethodHub>>();

            using (var client = new TestClient())
            {
                var endPointLifetime = endPoint.OnConnectedAsync(client.Connection);

                await client.Connected.OrTimeout();

                var result = (await client.InvokeAsync(nameof(MethodHub.HasHttpContext)).OrTimeout()).Result;
                Assert.False((bool)result);

                client.Dispose();

                await endPointLifetime.OrTimeout();
            }
        }

        private static void AssertHubMessage(HubMessage expected, HubMessage actual)
        {
            // We aren't testing InvocationIds here
            switch (expected)
            {
                case CompletionMessage expectedCompletion:
                    var actualCompletion = Assert.IsType<CompletionMessage>(actual);
                    Assert.Equal(expectedCompletion.Error, actualCompletion.Error);
                    Assert.Equal(expectedCompletion.HasResult, actualCompletion.HasResult);
                    Assert.Equal(expectedCompletion.Result, actualCompletion.Result);
                    break;
                case StreamItemMessage expectedStreamItem:
                    var actualStreamItem = Assert.IsType<StreamItemMessage>(actual);
                    Assert.Equal(expectedStreamItem.Item, actualStreamItem.Item);
                    break;
                case InvocationMessage expectedInvocation:
                    var actualInvocation = Assert.IsType<InvocationMessage>(actual);
                    Assert.Equal(expectedInvocation.NonBlocking, actualInvocation.NonBlocking);
                    Assert.Equal(expectedInvocation.Target, actualInvocation.Target);
                    Assert.Equal(expectedInvocation.Arguments, actualInvocation.Arguments);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported Hub Message type {expected.GetType()}");
            }
        }

        public static IEnumerable<object[]> HubTypes()
        {
            yield return new[] { typeof(DynamicTestHub) };
            yield return new[] { typeof(MethodHub) };
            yield return new[] { typeof(HubT) };
        }

        private static Type GetEndPointType(Type hubType)
        {
            var endPointType = typeof(HubEndPoint<>);
            return endPointType.MakeGenericType(hubType);
        }

        private static Type GetGenericType(Type genericType, Type hubType)
        {
            return genericType.MakeGenericType(hubType);
        }

        private IServiceProvider CreateServiceProvider(Action<ServiceCollection> addServices = null)
        {
            var services = new ServiceCollection();
            services.AddOptions()
                .AddLogging()
                .AddSignalR();

            addServices?.Invoke(services);

            return services.BuildServiceProvider();
        }

        private class DynamicTestHub : DynamicHub
        {
            public override Task OnConnectedAsync()
            {
                var tcs = (TaskCompletionSource<bool>)Context.Connection.Metadata["ConnectedTask"];
                tcs?.TrySetResult(true);
                return base.OnConnectedAsync();
            }

            public string Echo(string data)
            {
                return data;
            }

            public Task ClientSendMethod(string userId, string message)
            {
                return Clients.User(userId).Send(message);
            }

            public Task ConnectionSendMethod(string connectionId, string message)
            {
                return Clients.Client(connectionId).Send(message);
            }

            public Task GroupAddMethod(string groupName)
            {
                return Groups.AddAsync(Context.ConnectionId, groupName);
            }

            public Task GroupSendMethod(string groupName, string message)
            {
                return Clients.Group(groupName).Send(message);
            }

            public Task BroadcastMethod(string message)
            {
                return Clients.All.Broadcast(message);
            }

            public Task SendToAllExcept(string message, IReadOnlyList<string> excludedIds)
            {
                return Clients.AllExcept(excludedIds).Send(message);
            }
        }

        public interface Test
        {
            Task Send(string message);
            Task Broadcast(string message);
        }

        public class HubT : Hub<Test>
        {
            public override Task OnConnectedAsync()
            {
                var tcs = (TaskCompletionSource<bool>)Context.Connection.Metadata["ConnectedTask"];
                tcs?.TrySetResult(true);
                return base.OnConnectedAsync();
            }

            public string Echo(string data)
            {
                return data;
            }

            public Task ClientSendMethod(string userId, string message)
            {
                return Clients.User(userId).Send(message);
            }

            public Task ConnectionSendMethod(string connectionId, string message)
            {
                return Clients.Client(connectionId).Send(message);
            }
            public Task DelayedSend(string connectionId, string message)
            {
                Task.Delay(100);
                return Clients.Client(connectionId).Send(message);
            }
            public Task GroupAddMethod(string groupName)
            {
                return Groups.AddAsync(Context.ConnectionId, groupName);
            }

            public Task GroupSendMethod(string groupName, string message)
            {
                return Clients.Group(groupName).Send(message);
            }

            public Task BroadcastMethod(string message)
            {
                return Clients.All.Broadcast(message);
            }

            public Task SendToAllExcept(string message, IReadOnlyList<string> excludedIds)
            {
                return Clients.AllExcept(excludedIds).Send(message);
            }
        }

        public class StreamingHub : TestHub
        {
            public IObservable<string> CounterObservable(int count)
            {
                return new CountingObservable(count);
            }

            public ReadableChannel<string> CounterChannel(int count)
            {
                var channel = Channel.CreateUnbounded<string>();

                var task = Task.Run(async () =>
                {
                    for (int i = 0; i < count; i++)
                    {
                        await channel.Out.WriteAsync(i.ToString());
                    }
                    channel.Out.Complete();
                });

                return channel.In;
            }

            private class CountingObservable : IObservable<string>
            {
                private int _count;

                public CountingObservable(int count)
                {
                    _count = count;
                }

                public IDisposable Subscribe(IObserver<string> observer)
                {
                    var cts = new CancellationTokenSource();
                    Task.Run(() =>
                    {
                        for (int i = 0; !cts.Token.IsCancellationRequested && i < _count; i++)
                        {
                            observer.OnNext(i.ToString());
                        }
                        observer.OnCompleted();
                    });

                    return new CancellationDisposable(cts);
                }
            }
        }

        public class OnConnectedThrowsHub : Hub
        {
            public override Task OnConnectedAsync()
            {
                var tcs = new TaskCompletionSource<object>();
                tcs.SetException(new InvalidOperationException("Hub OnConnected failed."));
                return tcs.Task;
            }
        }

        public class OnDisconnectedThrowsHub : TestHub
        {
            public override Task OnDisconnectedAsync(Exception exception)
            {
                var tcs = new TaskCompletionSource<object>();
                tcs.SetException(new InvalidOperationException("Hub OnDisconnected failed."));
                return tcs.Task;
            }
        }

        private class MethodHub : TestHub
        {
            public Task GroupRemoveMethod(string groupName)
            {
                return Groups.RemoveAsync(Context.ConnectionId, groupName);
            }

            public Task ClientSendMethod(string userId, string message)
            {
                return Clients.User(userId).InvokeAsync("Send", message);
            }

            public Task ConnectionSendMethod(string connectionId, string message)
            {
                return Clients.Client(connectionId).InvokeAsync("Send", message);
            }

            public Task GroupAddMethod(string groupName)
            {
                return Groups.AddAsync(Context.ConnectionId, groupName);
            }

            public Task GroupSendMethod(string groupName, string message)
            {
                return Clients.Group(groupName).InvokeAsync("Send", message);
            }

            public Task BroadcastMethod(string message)
            {
                return Clients.All.InvokeAsync("Broadcast", message);
            }

            public Task BroadcastItem()
            {
                return Clients.All.InvokeAsync("Broadcast", new { Message = "test", paramName = "test" });
            }

            public Task<int> TaskValueMethod()
            {
                return Task.FromResult(42);
            }

            public int ValueMethod()
            {
                return 43;
            }

            public string Echo(string data)
            {
                return data;
            }

            public void VoidMethod()
            {
            }

            public string ConcatString(byte b, int i, char c, string s)
            {
                return $"{b}, {i}, {c}, {s}";
            }

            public override Task OnDisconnectedAsync(Exception e)
            {
                return Task.CompletedTask;
            }

            public void MethodThatThrows()
            {
                throw new InvalidOperationException("BOOM!");
            }

            public Task MethodThatYieldsFailedTask()
            {
                return Task.FromException(new InvalidOperationException("BOOM!"));
            }

            public static void StaticMethod()
            {
            }

            [Authorize("test")]
            public void AuthMethod()
            {
            }

            public Task SendToAllExcept(string message, IReadOnlyList<string> excludedIds)
            {
                return Clients.AllExcept(excludedIds).InvokeAsync("Send", message);
            }

            public bool HasHttpContext()
            {
                return Context.Connection.GetHttpContext() != null;
            }
        }

        private class InheritedHub : BaseHub
        {
            public override int VirtualMethod(int num)
            {
                return num - 10;
            }
        }

        private class BaseHub : TestHub
        {
            public string BaseMethod(string message)
            {
                return message;
            }

            public virtual int VirtualMethod(int num)
            {
                return num;
            }
        }

        private class InvalidHub : TestHub
        {
            public void OverloadedMethod(int num)
            {
            }

            public void OverloadedMethod(string message)
            {
            }
        }

        private class DisposeTrackingHub : TestHub
        {
            private TrackDispose _trackDispose;

            public DisposeTrackingHub(TrackDispose trackDispose)
            {
                _trackDispose = trackDispose;
            }

            protected override void Dispose(bool dispose)
            {
                if (dispose)
                {
                    _trackDispose.DisposeCount++;
                }
            }
        }

        private class TrackDispose
        {
            public int DisposeCount = 0;
        }

        public abstract class TestHub : Hub
        {
            public override Task OnConnectedAsync()
            {
                var tcs = (TaskCompletionSource<bool>)Context.Connection.Metadata["ConnectedTask"];
                tcs?.TrySetResult(true);
                return base.OnConnectedAsync();
            }
        }

        public class SimpleHub : Hub
        {
            public override async Task OnConnectedAsync()
            {
                await Clients.All.InvokeAsync("Send", $"{Context.ConnectionId} joined");
                await base.OnConnectedAsync();
            }
        }

        public interface ITypedHubClient
        {
            Task Send(string message);
        }

        public class SimpleTypedHub : Hub<ITypedHubClient>
        {
            public override async Task OnConnectedAsync()
            {
                await Clients.All.Send($"{Context.ConnectionId} joined");
                await base.OnConnectedAsync();
            }
        }
    }
}
