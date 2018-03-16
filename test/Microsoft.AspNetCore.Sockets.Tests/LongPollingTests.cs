// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.IO.Pipelines;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Sockets.Internal.Transports;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Microsoft.AspNetCore.Sockets.Tests
{
    public class LongPollingTests
    {
        [Fact]
        public async Task Set204StatusCodeWhenChannelComplete()
        {
            var pair = DuplexPipe.CreateConnectionPair(PipeOptions.Default, PipeOptions.Default);
            var connection = new DefaultConnectionContext("foo", pair.Transport, pair.Application);

            var context = new DefaultHttpContext();

            var poll = new LongPollingTransport(CancellationToken.None, connection.Application.Input, connectionId: string.Empty, loggerFactory: new LoggerFactory());

            connection.Transport.Output.Complete();

            await poll.ProcessRequestAsync(context, context.RequestAborted).OrTimeout();

            Assert.Equal(204, context.Response.StatusCode);
        }

        [Fact]
        public async Task Set200StatusCodeWhenTimeoutTokenFires()
        {
            var pair = DuplexPipe.CreateConnectionPair(PipeOptions.Default, PipeOptions.Default);
            var connection = new DefaultConnectionContext("foo", pair.Transport, pair.Application);
            var context = new DefaultHttpContext();

            var timeoutToken = new CancellationToken(true);
            var poll = new LongPollingTransport(timeoutToken, connection.Application.Input, connectionId: string.Empty, loggerFactory: new LoggerFactory());

            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(timeoutToken, context.RequestAborted))
            {
                await poll.ProcessRequestAsync(context, cts.Token).OrTimeout();

                Assert.Equal(0, context.Response.ContentLength);
                Assert.Equal(200, context.Response.StatusCode);
            }
        }

        [Fact]
        public async Task FrameSentAsSingleResponse()
        {
            var pair = DuplexPipe.CreateConnectionPair(PipeOptions.Default, PipeOptions.Default);
            var connection = new DefaultConnectionContext("foo", pair.Transport, pair.Application);
            var context = new DefaultHttpContext();

            var poll = new LongPollingTransport(CancellationToken.None, connection.Application.Input, connectionId: string.Empty, loggerFactory: new LoggerFactory());
            var ms = new MemoryStream();
            context.Response.Body = ms;

            await connection.Transport.Output.WriteAsync(Encoding.UTF8.GetBytes("Hello World"));
            connection.Transport.Output.Complete();

            await poll.ProcessRequestAsync(context, context.RequestAborted).OrTimeout();

            Assert.Equal(200, context.Response.StatusCode);
            Assert.Equal("Hello World", Encoding.UTF8.GetString(ms.ToArray()));
        }

        [Fact]
        public async Task MultipleFramesSentAsSingleResponse()
        {
            var pair = DuplexPipe.CreateConnectionPair(PipeOptions.Default, PipeOptions.Default);
            var connection = new DefaultConnectionContext("foo", pair.Transport, pair.Application);
            var context = new DefaultHttpContext();

            var poll = new LongPollingTransport(CancellationToken.None, connection.Application.Input, connectionId: string.Empty, loggerFactory: new LoggerFactory());
            var ms = new MemoryStream();
            context.Response.Body = ms;

            await connection.Transport.Output.WriteAsync(Encoding.UTF8.GetBytes("Hello"));
            await connection.Transport.Output.WriteAsync(Encoding.UTF8.GetBytes(" "));
            await connection.Transport.Output.WriteAsync(Encoding.UTF8.GetBytes("World"));

            connection.Transport.Output.Complete();

            await poll.ProcessRequestAsync(context, context.RequestAborted).OrTimeout();

            Assert.Equal(200, context.Response.StatusCode);

            var payload = ms.ToArray();
            Assert.Equal("Hello World", Encoding.UTF8.GetString(payload));
        }

        [Fact]
        public void CheckLongPollingTimeoutValue()
        {
            var options = new HttpSocketOptions();
            Assert.Equal(options.LongPolling.PollTimeout, TimeSpan.FromSeconds(90));
        }
    }
}
