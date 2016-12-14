using System;
using System.Collections.Generic;
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
                    throw new InvalidOperationException();
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
                catch (InvalidOperationException)
                {
                    exceptions++;
                }

                try
                {
                    await nextTask;
                }
                catch (InvalidOperationException)
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

                var idx = new HashSet<int>();
                var tasks = new List<Task>(5);
                for (var i = 0; i < 5; i++)
                {
                    int captureIndex = i;
                    tasks.Add(hubConnection.Enqueue(() =>
                    {
                        int index = captureIndex;
                        for (int j = 0; j < 5; j++)
                        {
                            // check that queued items run in sequence
                            if (index <= j)
                            {
                                Assert.False(idx.Contains(j));
                            }
                            else
                            {
                                Assert.True(idx.Contains(j));
                            }
                        }
                        idx.Add(index);
                        return TaskCache.CompletedTask;
                    }));
                }

                await Task.WhenAll(tasks);

                // confirm all queued items were run
                Assert.Equal(5, idx.Count);
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
