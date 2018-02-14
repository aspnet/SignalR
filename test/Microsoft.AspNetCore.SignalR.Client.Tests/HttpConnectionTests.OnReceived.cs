// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.IO.Pipelines;
using Microsoft.AspNetCore.Client.Tests;
using Xunit;

namespace Microsoft.AspNetCore.SignalR.Client.Tests
{
    public partial class HttpConnectionTests
    {
        public class OnReceived
        {
            [Fact]
            public async Task CanReceiveData()
            {
                var testHttpHandler = new TestHttpMessageHandler();

                testHttpHandler.OnLongPoll(cancellationToken => ResponseUtils.CreateResponse(HttpStatusCode.OK, "42"));
                testHttpHandler.OnSocketSend((_, __) => ResponseUtils.CreateResponse(HttpStatusCode.Accepted));

                await WithConnectionAsync(
                    CreateConnection(testHttpHandler),
                    async (connection, closed) =>
                    {
                        await connection.StartAsync().OrTimeout();
                        var data = await connection.Input.ReadSingleAsync().OrTimeout();
                        Assert.Contains("42", Encoding.UTF8.GetString(data));
                    });
            }
        }
    }
}
