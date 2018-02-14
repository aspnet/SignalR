// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Net;
using System.Text;
using System.Threading.Tasks;
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

                        async Task<string> ReadAsync()
                        {
                            var result = await connection.Input.ReadAsync();
                            var buffer = result.Buffer;

                            try
                            {
                                return Encoding.UTF8.GetString(buffer.ToArray());
                            }
                            finally
                            {
                                connection.Input.AdvanceTo(buffer.End);
                            }
                        }

                        await connection.StartAsync().OrTimeout();
                        Assert.Contains("42", await ReadAsync().OrTimeout());
                    });
            }
        }
    }
}
