// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.AspNetCore.SignalR.Client.Tests
{
    public partial class HttpConnectionTests
    {
        // Nested class for grouping
        public class AbortAsync
        {
            [Fact]
            public Task AbortAsyncTriggersClosedEventWithException()
            {
                return WithConnectionAsync(CreateConnection(), async (connection, closed) =>
                {
                    // Start the connection
                    await connection.StartAsync().OrTimeout();

                    // Abort with an error
                    var expected = new Exception("Ruh roh!");
                    await connection.AbortAsync(expected).OrTimeout();

                    // Verify that it is thrown
                    var actual = await Assert.ThrowsAsync<Exception>(async () => await closed.OrTimeout());
                    Assert.Same(expected, actual);
                });
            }
        }
    }
}
