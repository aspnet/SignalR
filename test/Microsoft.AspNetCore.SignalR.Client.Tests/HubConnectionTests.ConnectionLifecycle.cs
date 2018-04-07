using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.AspNetCore.SignalR.Client.Tests
{
    public partial class HubConnectionTests
    {
        public class ConnectionLifecycle
        {
            // This tactic (using names and a dictionary) allows non-serializable data (like a Func) to be used in a theory AND get it to show in the new hierarchical view in Test Explorer as separate tests you can run individually.
            private static readonly IDictionary<string, Func<HubConnection, Task>> MethodsThatRequireActiveConnection = new Dictionary<string, Func<HubConnection, Task>>()
            {
                { nameof(HubConnection.InvokeAsync), (connection) => connection.InvokeAsync("Foo") },
                { nameof(HubConnection.SendAsync), (connection) => connection.SendAsync("Foo") },
                { nameof(HubConnection.StreamAsChannelAsync), (connection) => connection.StreamAsChannelAsync<object>("Foo") },
            };

            public static IEnumerable<object[]> MethodsNamesThatRequireActiveConnection => MethodsThatRequireActiveConnection.Keys.Select(k => new object[] { k });

            private HubConnection CreateHubConnection(TestConnection testConnection)
            {
                var builder = new HubConnectionBuilder();
                builder.WithConnectionFactory(format => testConnection.StartAsync(format));
                return builder.Build();
            }

            private HubConnection CreateHubConnection(Func<TransferFormat, Task<ConnectionContext>> connectionFactory)
            {
                var builder = new HubConnectionBuilder();
                builder.WithConnectionFactory(format => connectionFactory(format));
                return builder.Build();
            }

            [Fact]
            public async Task StartAsyncStartsTheUnderlyingConnection()
            {
                var testConnection = new TestConnection();
                await AsyncUsing(CreateHubConnection(testConnection), async connection =>
                {
                    await connection.StartAsync();
                    Assert.True(testConnection.Started.IsCompleted);
                });
            }

            [Fact]
            public async Task StartAsyncWaitsForPreviousStartIfAlreadyStarting()
            {
                // Set up StartAsync to wait on the syncPoint when starting
                var testConnection = new TestConnection(onStart: SyncPoint.Create(out var syncPoint));
                await AsyncUsing(CreateHubConnection(testConnection), async connection =>
                {
                    var firstStart = connection.StartAsync().OrTimeout();
                    Assert.False(firstStart.IsCompleted);

                    // Wait for us to be in IConnectionFactory.ConnectAsync
                    await syncPoint.WaitForSyncPoint();

                    // Try starting again
                    var secondStart = connection.StartAsync().OrTimeout();
                    Assert.False(secondStart.IsCompleted);

                    // Release the sync point
                    syncPoint.Continue();

                    // Both starts should finish fine
                    await firstStart;
                    await secondStart;
                });
            }

            [Fact]
            public async Task StartingAfterStopCreatesANewConnection()
            {
                // Set up StartAsync to wait on the syncPoint when starting
                var createCount = 0;
                Task<ConnectionContext> ConnectionFactory(TransferFormat format)
                {
                    createCount += 1;
                    return new TestConnection().StartAsync(format);
                }

                await AsyncUsing(CreateHubConnection(ConnectionFactory), async connection =>
                {
                    await connection.StartAsync().OrTimeout();
                    Assert.Equal(1, createCount);
                    await connection.StopAsync().OrTimeout();

                    await connection.StartAsync().OrTimeout();
                    Assert.Equal(2, createCount);
                });
            }

            [Fact]
            public async Task StartAsyncWithFailedHandshakeCanBeStopped()
            {
                var testConnection = new TestConnection(autoHandshake: false);
                await AsyncUsing(CreateHubConnection(testConnection), async connection =>
                {
                    testConnection.Transport.Input.Complete();
                    try
                    {
                        await connection.StartAsync();
                    }
                    catch
                    { }

                    await connection.StopAsync();
                    Assert.True(testConnection.Started.IsCompleted);
                });
            }

            [Theory]
            [MemberData(nameof(MethodsNamesThatRequireActiveConnection))]
            public async Task MethodsThatRequireStartedConnectionFailIfConnectionNotYetStarted(string name)
            {
                var method = MethodsThatRequireActiveConnection[name];

                var testConnection = new TestConnection();
                await AsyncUsing(CreateHubConnection(testConnection), async connection =>
                {
                    var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => method(connection));
                    Assert.Equal($"The '{name}' method cannot be called if the connection is not active", ex.Message);
                });
            }

            [Theory]
            [MemberData(nameof(MethodsNamesThatRequireActiveConnection))]
            public async Task MethodsThatRequireStartedConnectionWaitForStartIfConnectionIsCurrentlyStarting(string name)
            {
                var method = MethodsThatRequireActiveConnection[name];

                // Set up StartAsync to wait on the syncPoint when starting
                var testConnection = new TestConnection(onStart: SyncPoint.Create(out var syncPoint));
                await AsyncUsing(CreateHubConnection(testConnection), async connection =>
                {
                    // Start, and wait for the sync point to be hit
                    var startTask = connection.StartAsync().OrTimeout();
                    Assert.False(startTask.IsCompleted);
                    await syncPoint.WaitForSyncPoint();

                    // Run the method, but it will be waiting for the lock
                    var targetTask = method(connection).OrTimeout();

                    // Release the SyncPoint
                    syncPoint.Continue();

                    // Wait for start to finish
                    await startTask;

                    // We need some special logic to ensure InvokeAsync completes.
                    if (string.Equals(name, nameof(HubConnection.InvokeAsync)))
                    {
                        await ForceLastInvocationToComplete(testConnection);
                    }

                    // Wait for the method to complete.
                    await targetTask;
                });
            }

            [Fact]
            public async Task StopAsyncStopsConnection()
            {
                var testConnection = new TestConnection();
                await AsyncUsing(CreateHubConnection(testConnection), async connection =>
                {
                    await connection.StartAsync().OrTimeout();
                    Assert.True(testConnection.Started.IsCompleted);

                    await connection.StopAsync().OrTimeout();
                    await testConnection.Disposed.OrTimeout();
                });
            }

            [Fact]
            public async Task StopAsyncNoOpsIfConnectionNotYetStarted()
            {
                var testConnection = new TestConnection();
                await AsyncUsing(CreateHubConnection(testConnection), async connection =>
                {
                    await connection.StopAsync().OrTimeout();
                    Assert.False(testConnection.Disposed.IsCompleted);
                });
            }

            [Fact]
            public async Task StopAsyncNoOpsIfConnectionAlreadyStopped()
            {
                var testConnection = new TestConnection();
                await AsyncUsing(CreateHubConnection(testConnection), async connection =>
                {
                    await connection.StartAsync().OrTimeout();
                    Assert.True(testConnection.Started.IsCompleted);

                    await connection.StopAsync().OrTimeout();
                    await testConnection.Disposed.OrTimeout();

                    await connection.StopAsync().OrTimeout();
                });
            }

            [Fact]
            public async Task CompletingTheTransportSideMarksConnectionAsClosed()
            {
                var testConnection = new TestConnection();
                var closed = new TaskCompletionSource<object>();
                await AsyncUsing(CreateHubConnection(testConnection), async connection =>
                {
                    connection.Closed += (e) => closed.TrySetResult(null);
                    await connection.StartAsync().OrTimeout();
                    Assert.True(testConnection.Started.IsCompleted);

                    // Complete the transport side and wait for the connection to close
                    testConnection.CompleteFromTransport();
                    await closed.Task.OrTimeout();

                    // We should be stopped now
                    var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => connection.SendAsync("Foo").OrTimeout());
                    Assert.Equal($"The '{nameof(HubConnection.SendAsync)}' method cannot be called if the connection is not active", ex.Message);
                });
            }

            [Fact]
            public async Task TransportCompletionWhileShuttingDownIsNoOp()
            {
                var testConnection = new TestConnection();
                var testConnectionClosed = new TaskCompletionSource<object>();
                var connectionClosed = new TaskCompletionSource<object>();
                await AsyncUsing(CreateHubConnection(testConnection), async connection =>
                {
                    // We're hooking the TestConnection shutting down here because the HubConnection one will be blocked on the lock
                    testConnection.Transport.Input.OnWriterCompleted((_, __) => testConnectionClosed.TrySetResult(null), null);
                    connection.Closed += (e) => connectionClosed.TrySetResult(null);

                    await connection.StartAsync().OrTimeout();
                    Assert.True(testConnection.Started.IsCompleted);

                    // Start shutting down and complete the transport side
                    var stopTask = connection.StopAsync().OrTimeout();
                    testConnection.CompleteFromTransport();

                    // Wait for the connection to close.
                    await testConnectionClosed.Task.OrTimeout();

                    // The stop should be completed.
                    await stopTask;

                    // The HubConnection should now be closed.
                    await connectionClosed.Task.OrTimeout();

                    // We should be stopped now
                    var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => connection.SendAsync("Foo").OrTimeout());
                    Assert.Equal($"The '{nameof(HubConnection.SendAsync)}' method cannot be called if the connection is not active", ex.Message);

                    await testConnection.Disposed.OrTimeout();

                    Assert.Equal(1, testConnection.DisposeCount);
                });
            }

            [Fact]
            public async Task StopAsyncDuringUnderlyingConnectionCloseWaitsAndNoOps()
            {
                var testConnection = new TestConnection();
                var connectionClosed = new TaskCompletionSource<object>();
                await AsyncUsing(CreateHubConnection(testConnection), async connection =>
                {
                    connection.Closed += (e) => connectionClosed.TrySetResult(null);

                    await connection.StartAsync().OrTimeout();
                    Assert.True(testConnection.Started.IsCompleted);

                    // Complete the transport side and wait for the connection to close
                    testConnection.CompleteFromTransport();

                    // Start stopping manually (these can't be synchronized by a Sync Point because the transport is disposed outside the lock)
                    var stopTask = connection.StopAsync().OrTimeout();

                    await testConnection.Disposed.OrTimeout();

                    // Wait for the stop task to complete and the closed event to fire
                    await stopTask;
                    await connectionClosed.Task.OrTimeout();

                    // We should be stopped now
                    var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => connection.SendAsync("Foo").OrTimeout());
                    Assert.Equal($"The '{nameof(HubConnection.SendAsync)}' method cannot be called if the connection is not active", ex.Message);
                });
            }

            [Theory]
            [MemberData(nameof(MethodsNamesThatRequireActiveConnection))]
            public async Task MethodsThatRequireActiveConnectionWaitForStopAndFailIfConnectionIsCurrentlyStopping(string methodName)
            {
                var method = MethodsThatRequireActiveConnection[methodName];

                // Set up StartAsync to wait on the syncPoint when starting
                var testConnection = new TestConnection(onDispose: SyncPoint.Create(out var syncPoint));
                await AsyncUsing(CreateHubConnection(testConnection), async connection =>
                {
                    await connection.StartAsync().OrTimeout();

                    // Stop and invoke the method. These two aren't synchronizable via a Sync Point any more because the transport is disposed
                    // outside the lock :(
                    var disposeTask = connection.StopAsync().OrTimeout();
                    var targetTask = method(connection).OrTimeout();

                    // Release the sync point
                    syncPoint.Continue();

                    // Wait for the method to complete, with an expected error.
                    var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => targetTask);
                    Assert.Equal($"The '{methodName}' method cannot be called if the connection is not active", ex.Message);

                    await disposeTask;
                });
            }

            [Fact]
            public async Task ClientTimesoutWhenHandshakeResponseTakesTooLong()
            {
                var connection = new TestConnection(autoHandshake: false);
                var hubConnection = CreateHubConnection(connection);
                try
                {
                    hubConnection.HandshakeTimeout = TimeSpan.FromMilliseconds(1);

                    await Assert.ThrowsAsync<OperationCanceledException>(() => hubConnection.StartAsync().OrTimeout());
                }
                finally
                {
                    await hubConnection.DisposeAsync().OrTimeout();
                    await connection.DisposeAsync().OrTimeout();
                }
            }

            [Fact]
            public async Task StartAsyncWithTriggeredCancellationTokenIsCanceled()
            {
                var onStartCalled = false;
                var connection = new TestConnection(onStart: () =>
                {
                    onStartCalled = true;
                    return Task.CompletedTask;
                });
                var hubConnection = CreateHubConnection(connection);
                try
                {
                    await Assert.ThrowsAsync<OperationCanceledException>(() => hubConnection.StartAsync(new CancellationToken(canceled: true)).OrTimeout());
                    Assert.False(onStartCalled);
                }
                finally
                {
                    await hubConnection.DisposeAsync().OrTimeout();
                    await connection.DisposeAsync().OrTimeout();
                }
            }

            [Fact]
            public async Task StartAsyncCanTriggerCancellationTokenToCancelHandshake()
            {
                var cts = new CancellationTokenSource();
                var connection = new TestConnection(onStart: () =>
                {
                    cts.Cancel();
                    return Task.CompletedTask;
                }, autoHandshake: false);
                var hubConnection = CreateHubConnection(connection);
                // We want to make sure the cancellation is because of the token passed to StartAsync
                hubConnection.HandshakeTimeout = Timeout.InfiniteTimeSpan;
                try
                {
                    var startTask = hubConnection.StartAsync(cts.Token);
                    var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => startTask.OrTimeout());
                    Assert.Equal("The operation was canceled.", exception.Message);
                }
                finally
                {
                    await hubConnection.DisposeAsync().OrTimeout();
                    await connection.DisposeAsync().OrTimeout();
                }
            }

            private static async Task ForceLastInvocationToComplete(TestConnection testConnection)
            {
                // We need to "complete" the invocation
                var message = await testConnection.ReadSentTextMessageAsync();
                var json = JObject.Parse(message); // Gotta remove the record separator.
                await testConnection.ReceiveJsonMessage(new
                {
                    type = HubProtocolConstants.CompletionMessageType,
                    invocationId = json["invocationId"],
                });
            }

            // A helper that we wouldn't want to use in product code, but is fine for testing until IAsyncDisposable arrives :)
            private static async Task AsyncUsing(HubConnection connection, Func<HubConnection, Task> action)
            {
                try
                {
                    await action(connection);
                }
                finally
                {
                    // Dispose isn't under test here, so fire and forget so that errors/timeouts here don't cause
                    // test errors that mask the real errors.
                    _ = connection.DisposeAsync();
                }
            }
        }
    }
}
