// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.AspNetCore.SignalR.Client.Tests
{
    public partial class HttpConnectionTests
    {
        // Nested class for grouping
        public class AbortAsync : LoggedTest
        {
            public AbortAsync(ITestOutputHelper output) : base(output)
            {
            }

            [Fact]
            public async Task AbortAsyncTriggersClosedEventWithException()
            {
                using (StartLog(out var loggerFactory))
                {
                    await WithConnectionAsync(CreateConnection(loggerFactory: loggerFactory), async (connection, closed) =>
                    {
                        var logger = loggerFactory.CreateLogger<AbortAsync>();

                        // Start the connection
                        logger.LogInformation("Starting connection");
                        await connection.StartAsync().OrTimeout();
                        logger.LogInformation("Started connection");

                        // Abort with an error
                        logger.LogInformation("Aborting connection");
                        var expected = new Exception("Ruh roh!");
                        await connection.AbortAsync(expected).OrTimeout();
                        logger.LogInformation("Aborted connection");

                        // Verify that it is thrown
                        logger.LogInformation("Verifying that closed throws the exception from Abort");
                        var actual = await Assert.ThrowsAsync<Exception>(async () => await closed.OrTimeout());
                        Assert.Same(expected, actual);
                    });
                }
            }

            [Fact]
            public async Task AbortAsyncWhileStoppingTriggersClosedEventWithException()
            {
                using (StartLog(out var loggerFactory))
                {
                    await WithConnectionAsync(CreateConnection(loggerFactory: loggerFactory, transport: new TestTransport(onTransportStop: SyncPoint.Create(2, out var syncPoints))), async (connection, closed) =>
                    {
                        var logger = loggerFactory.CreateLogger<AbortAsync>();

                        // Start the connection
                        logger.LogInformation("Starting connection");
                        await connection.StartAsync().OrTimeout();
                        logger.LogInformation("Started connection");

                        // Stop normally
                        logger.LogInformation("Stopping connection");
                        var stopTask = connection.StopAsync().OrTimeout();

                        // Wait to reach the first sync point
                        logger.LogInformation("Waiting for transport to shut down");
                        await syncPoints[0].WaitForSyncPoint().OrTimeout();
                        logger.LogInformation("Transport is now shutting down");

                        // Abort with an error
                        logger.LogInformation("Aborting connection");
                        var expected = new Exception("Ruh roh!");
                        var abortTask = connection.AbortAsync(expected).OrTimeout();

                        // Wait for the sync point to hit again
                        logger.LogInformation("Waiting for transport to shut down (again)");
                        await syncPoints[1].WaitForSyncPoint().OrTimeout();
                        logger.LogInformation("Transport is now shutting down (again)");

                        // Release sync point 0
                        syncPoints[0].Continue();

                        logger.LogInformation("Verifying that closed throws the exception from Abort");
                        var actual = await Assert.ThrowsAsync<Exception>(async () => await closed.OrTimeout());
                        Assert.Same(expected, actual);

                        logger.LogInformation("Cleaning up");
                        syncPoints[1].Continue();
                        await Task.WhenAll(stopTask, abortTask).OrTimeout();
                    });
                }
            }

            [Fact]
            public async Task StopAsyncWhileAbortingTriggersClosedEventWithoutException()
            {
                using (StartLog(out var loggerFactory))
                {
                    await WithConnectionAsync(CreateConnection(loggerFactory: loggerFactory, transport: new TestTransport(onTransportStop: SyncPoint.Create(2, out var syncPoints))), async (connection, closed) =>
                    {
                        var logger = loggerFactory.CreateLogger<AbortAsync>();

                        logger.LogInformation("Starting connection");
                        await connection.StartAsync().OrTimeout();
                        logger.LogInformation("Started connection");

                        logger.LogInformation("Aborting connection");
                        var expected = new Exception("Ruh roh!");
                        var abortTask = connection.AbortAsync(expected).OrTimeout();

                        logger.LogInformation("Waiting for transport to shut down");
                        await syncPoints[0].WaitForSyncPoint().OrTimeout();
                        logger.LogInformation("Transport is now shutting down");

                        // Stop normally, without a sync point.
                        syncPoints[1].Continue();

                        logger.LogInformation("Stopping gracefully");
                        await connection.StopAsync();
                        logger.LogInformation("Verifying closed event does not throw");
                        await closed.OrTimeout();

                        logger.LogInformation("Cleaning up");
                        syncPoints[0].Continue();
                        await abortTask.OrTimeout();
                    });
                }
            }

            [Fact]
            public async Task StartAsyncCannotBeCalledWhileAbortAsyncInProgress()
            {
                using (StartLog(out var loggerFactory))
                {
                    await WithConnectionAsync(CreateConnection(loggerFactory: loggerFactory, transport: new TestTransport(onTransportStop: SyncPoint.Create(out var syncPoint))), async (connection, closed) =>
                    {
                        var logger = loggerFactory.CreateLogger<AbortAsync>();

                        // Start the connection
                        logger.LogInformation("Starting connection");
                        await connection.StartAsync().OrTimeout();
                        logger.LogInformation("Started connection");

                        // Abort with an error
                        var expected = new Exception("Ruh roh!");
                        logger.LogInformation("Aborting connection");
                        var abortTask = connection.AbortAsync(expected).OrTimeout();

                        // Wait to reach the first sync point
                        logger.LogInformation("Waiting for transport to shut down");
                        await syncPoint.WaitForSyncPoint().OrTimeout();
                        logger.LogInformation("Transport is now shutting down");

                        logger.LogInformation("Verifying that start throws");
                        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => connection.StartAsync().OrTimeout());
                        Assert.Equal("Cannot start a connection that is not in the Disconnected state.", ex.Message);
                        logger.LogInformation("Verified that start throws");

                        // Release the sync point and wait for close to complete
                        // (it will throw the abort exception)
                        logger.LogInformation("Allowing Abort to continue");
                        syncPoint.Continue();
                        await abortTask.OrTimeout();
                        logger.LogInformation("Abort completed");

                        logger.LogInformation("Verified that abort exception was given to Closed handler");
                        var actual = await Assert.ThrowsAsync<Exception>(() => closed.OrTimeout());
                        Assert.Same(expected, actual);

                        logger.LogInformation("Verifying that connection can be started again after Abort completes");
                        await connection.StartAsync().OrTimeout();

                        logger.LogInformation("Verifying that we can stop without getting the abort exception");
                        await connection.StopAsync().OrTimeout();
                    });
                }
            }
        }
    }
}
