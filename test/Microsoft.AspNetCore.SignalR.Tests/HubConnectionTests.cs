using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Internal;
using Xunit;

namespace Microsoft.AspNetCore.SignalR.Tests
{
    public class HubConnectionTests
    {
        [Fact]
        public async Task QueuedTaskThrows_EndsOtherQueuedTasks()
        {
            using (var connectionWrapper = new TestHelpers.ConnectionWrapper())
            {
                var serviceProvider = TestHelpers.CreateServiceProvider();
                var hubConnection = new HubConnection(connectionWrapper.Connection, serviceProvider.GetService<InvocationAdapterRegistry>());

                var throwTask = hubConnection.Enqueue(() =>
                {
                    throw new Exception();
                });
                var nextTask = hubConnection.Enqueue(() =>
                {
                    // shouldn't be called because previous task threw
                    Assert.True(false);
                    return TaskCache.CompletedTask;
                });

                var exceptions = 0;
                try
                {
                    await throwTask;
                }
                catch (Exception)
                {
                    exceptions++;
                }

                try
                {
                    await nextTask;
                }
                catch (Exception)
                {
                    exceptions++;
                }

                Assert.Equal(2, exceptions);
            }
        }

        [Fact]
        public async Task CanHandleConcurrentWrites()
        {
            using (var connectionWrapper = new TestHelpers.ConnectionWrapper())
            {
                var serviceProvider = TestHelpers.CreateServiceProvider();
                var hubConnection = new HubConnection(connectionWrapper.Connection, serviceProvider.GetService<InvocationAdapterRegistry>());

                var tasks = new List<Task>(5);
                var events = new List<ManualResetEvent>(5);
                for (var i = 0; i < 5; i++)
                {
                    events.Add(new ManualResetEvent(false));
                    int captureIndex = i;
                    tasks.Add(hubConnection.Enqueue(() =>
                    {
                        int index = captureIndex;
                        for (int j = 0; j < 5; j++)
                        {
                            // check that queued items run in sequence
                            if (index <= j)
                            {
                                Assert.False(events[j].WaitOne(0));
                            }
                            else
                            {
                                Assert.True(events[j].WaitOne(0));
                            }
                        }
                        events[index].Set();
                        return TaskCache.CompletedTask;
                    }));
                }

                await Task.WhenAll(tasks);

                // confirm all queued items were run
                foreach (var e in events)
                {
                    Assert.True(e.WaitOne(0));
                }
            }
        }

        [Fact]
        public async Task ClosedConnection_DoesNotQueueTasks()
        {
            using (var connectionWrapper = new TestHelpers.ConnectionWrapper())
            {
                var serviceProvider = TestHelpers.CreateServiceProvider();
                var hubConnection = new HubConnection(connectionWrapper.Connection, serviceProvider.GetService<InvocationAdapterRegistry>());

                var called = false;
                await hubConnection.Enqueue(() =>
                {
                    called = true;
                    return TaskCache.CompletedTask;
                });

                Assert.True(called);

                hubConnection.Close();

                await hubConnection.Enqueue(() =>
                {
                    Assert.True(false);
                    return TaskCache.CompletedTask;
                });
            }
        }
    }
}
