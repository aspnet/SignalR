using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.AspNetCore.SignalR.Client.Tests
{
    public partial class HubConnectionTests
    {
        public class ConnectionLifecycle
        {
            // This tactic (using names and a dictionary) allows non-serializable data (like a Func) to be used in a theory AND get it to show in the new hierarchical view in Test Explorer as separate tests you can run individually.
            private static IDictionary<string, Func<HubConnection, Task>> MethodsThatRequireActiveConnection = new Dictionary<string, Func<HubConnection, Task>>()
            {
                { nameof(HubConnection.InvokeAsync), (connection) => connection.InvokeAsync("Foo") },
                { nameof(HubConnection.SendAsync), (connection) => connection.SendAsync("Foo") },
                { nameof(HubConnection.StreamAsChannelAsync), (connection) => connection.StreamAsChannelAsync<object>("Foo") },
            };

            public static IEnumerable<object[]> MethodsNamesThatRequireActiveConnection => MethodsThatRequireActiveConnection.Keys.Select(k => new object[] { k });

            [Fact]
            public async Task StartAsyncStartsTheUnderlyingConnection()
            {
                var testConnection = new TestConnection();
                await AsyncUsing(new HubConnection(() => testConnection, new JsonHubProtocol()), async connection =>
                {
                    await connection.StartAsync();
                    Assert.True(testConnection.Started.IsCompleted);
                });
            }

            [Fact]
            public async Task StartAsyncFailsIfAlreadyStarting()
            {
                // Set up StartAsync to wait on the syncPoint when starting
                var testConnection = new TestConnection(onStart: SyncPoint.Create(out var syncPoint));
                await AsyncUsing(new HubConnection(() => testConnection, new JsonHubProtocol()), async connection =>
                {
                    var firstStart = connection.StartAsync().OrTimeout();
                    Assert.False(firstStart.IsCompleted);

                    // Wait for us to be in IConnection.StartAsync
                    await syncPoint.WaitForSyncPoint();

                    // Try starting again
                    var secondStart = connection.StartAsync().OrTimeout();
                    Assert.False(secondStart.IsCompleted);

                    // Release the sync point
                    syncPoint.Continue();

                    // First start should finish fine
                    await firstStart;

                    // Second start should have thrown
                    var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => secondStart);
                    Assert.Equal($"The '{nameof(HubConnection.StartAsync)}' method cannot be called if the connection has already been started.", ex.Message);
                });
            }

            [Theory]
            [MemberData(nameof(MethodsNamesThatRequireActiveConnection))]
            public async Task MethodsThatRequireStartedConnectionFailIfConnectionNotYetStarted(string name)
            {
                var method = MethodsThatRequireActiveConnection[name];

                var testConnection = new TestConnection();
                await AsyncUsing(new HubConnection(() => testConnection, new JsonHubProtocol()), async connection =>
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
                await AsyncUsing(new HubConnection(() => testConnection, new JsonHubProtocol()), async connection =>
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
                await AsyncUsing(new HubConnection(() => testConnection, new JsonHubProtocol()), async connection =>
                {
                    await connection.StartAsync().OrTimeout();
                    Assert.True(testConnection.Started.IsCompleted);

                    await connection.StopAsync().OrTimeout();
                    Assert.True(testConnection.Disposed.IsCompleted);
                });
            }

            [Fact]
            public async Task StopAsyncNoOpsIfConnectionNotYetStarted()
            {
                var testConnection = new TestConnection();
                await AsyncUsing(new HubConnection(() => testConnection, new JsonHubProtocol()), async connection =>
                {
                    await connection.StopAsync().OrTimeout();
                    Assert.False(testConnection.Disposed.IsCompleted);
                });
            }

            [Fact]
            public async Task StopAsyncNoOpsIfConnectionAlreadyStopped()
            {
                var testConnection = new TestConnection();
                await AsyncUsing(new HubConnection(() => testConnection, new JsonHubProtocol()), async connection =>
                {
                    await connection.StartAsync().OrTimeout();
                    Assert.True(testConnection.Started.IsCompleted);

                    await connection.StopAsync().OrTimeout();
                    Assert.True(testConnection.Disposed.IsCompleted);

                    await connection.StopAsync().OrTimeout();
                });
            }

            [Fact]
            public async Task ClosedEventTriggersConnectionToStop()
            {
                var testConnection = new TestConnection();
                var closed = new TaskCompletionSource<object>();
                await AsyncUsing(new HubConnection(() => testConnection, new JsonHubProtocol()), async connection =>
                {
                    connection.Closed += (e) => closed.TrySetResult(null);
                    await connection.StartAsync().OrTimeout();
                    Assert.True(testConnection.Started.IsCompleted);

                    // This will trigger the closed event
                    testConnection.ReceivedMessages.TryComplete();
                    await closed.Task.OrTimeout();

                    // We should be stopped now
                    var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => connection.SendAsync("Foo").OrTimeout());
                    Assert.Equal($"The '{nameof(HubConnection.SendAsync)}' method cannot be called if the connection is not active", ex.Message);
                });
            }

            [Fact]
            public async Task ClosedEventWhileShuttingDownIsNoOp()
            {
                var testConnection = new TestConnection(onDispose: SyncPoint.Create(out var syncPoint));
                var testConnectionClosed = new TaskCompletionSource<object>();
                var connectionClosed = new TaskCompletionSource<object>();
                await AsyncUsing(new HubConnection(() => testConnection, new JsonHubProtocol()), async connection =>
                {
                    // We're hooking the TestConnection Closed event here because the HubConnection one will be blocked on the lock
                    testConnection.Closed += (c, e) => testConnectionClosed.TrySetResult(null);
                    connection.Closed += (e) => connectionClosed.TrySetResult(null);

                    await connection.StartAsync().OrTimeout();
                    Assert.True(testConnection.Started.IsCompleted);

                    // Start shutting down, but hold at the sync point
                    var stopTask = connection.StopAsync().OrTimeout();
                    Assert.False(stopTask.IsCompleted);
                    await syncPoint.WaitForSyncPoint();

                    // Now trigger the closed event and wait for the underlying connection to close
                    testConnection.ReceivedMessages.TryComplete();
                    await testConnectionClosed.Task.OrTimeout();

                    // Now, complete the StopAsync
                    syncPoint.Continue();
                    await stopTask;

                    // The HubConnection should now be closed.
                    await connectionClosed.Task.OrTimeout();

                    // We should be stopped now
                    var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => connection.SendAsync("Foo").OrTimeout());
                    Assert.Equal($"The '{nameof(HubConnection.SendAsync)}' method cannot be called if the connection is not active", ex.Message);

                    Assert.Equal(1, testConnection.DisposeCount);
                });
            }

            [Fact]
            public async Task StopAsyncDuringUnderlyingConnectionCloseWaitsAndNoOps()
            {
                var testConnection = new TestConnection(onDispose: SyncPoint.Create(out var syncPoint));
                var connectionClosed = new TaskCompletionSource<object>();
                await AsyncUsing(new HubConnection(() => testConnection, new JsonHubProtocol()), async connection =>
                {
                    connection.Closed += (e) => connectionClosed.TrySetResult(null);

                    await connection.StartAsync().OrTimeout();
                    Assert.True(testConnection.Started.IsCompleted);

                    // Trigger the closed event and wait for the underlying connection to close
                    testConnection.ReceivedMessages.TryComplete();

                    // Wait for the HubConnection to start disposing the underlying connection (in response to the Closed event firing)
                    await syncPoint.WaitForSyncPoint();

                    // Start stopping manually
                    var stopTask = connection.StopAsync().OrTimeout();
                    Assert.False(stopTask.IsCompleted);

                    // Now, complete the StopAsync and wait for TestConnection to be fully disposed
                    syncPoint.Continue();
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
            public async Task MethodsThatRequireActiveConnectionWaitForStopAndFailIfConnectionIsCurrentlyStopping(string name)
            {
                var method = MethodsThatRequireActiveConnection[name];

                // Set up StartAsync to wait on the syncPoint when starting
                var testConnection = new TestConnection(onDispose: SyncPoint.Create(out var syncPoint));
                await AsyncUsing(new HubConnection(() => testConnection, new JsonHubProtocol()), async connection =>
                {
                    await connection.StartAsync().OrTimeout();

                    // Stop, but wait at the sync point.
                    var disposeTask = connection.StopAsync().OrTimeout();
                    await syncPoint.WaitForSyncPoint();

                    // Now start invoking the method under test
                    var targetTask = method(connection).OrTimeout();
                    Assert.False(targetTask.IsCompleted);

                    // Release the sync point
                    syncPoint.Continue();

                    // Wait for the method to complete, with an expected error.
                    var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => targetTask);
                    Assert.Equal($"The '{name}' method cannot be called if the connection is not active", ex.Message);

                    await disposeTask;
                });
            }

            private static async Task ForceLastInvocationToComplete(TestConnection testConnection)
            {
                // Dump the handshake message
                _ = await testConnection.SentMessages.ReadAsync();

                // Send a response
                await testConnection.ReceiveJsonMessage(new { });

                // We need to "complete" the invocation
                var message = await testConnection.ReadSentTextMessageAsync();
                var json = JObject.Parse(message.Substring(0, message.Length - 1)); // Gotta remove the record separator.
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
                    await connection.DisposeAsync();
                }
            }
        }
    }
}
