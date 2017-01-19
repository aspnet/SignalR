// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.IO.Pipelines;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Channels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Sockets.Transports;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Microsoft.AspNetCore.Sockets.Tests
{
    public class ServerSentEventsTests
    {
        [Fact]
        public async Task SSESetsContentType()
        {
            var channel = Channel.CreateUnbounded<Message>();
            var context = new DefaultHttpContext();
            var sse = new ServerSentEventsTransport(channel, new LoggerFactory());

            Assert.True(channel.Out.TryComplete());

            await sse.ProcessRequestAsync(context);

            Assert.Equal("text/event-stream", context.Response.ContentType);
            Assert.Equal("no-cache", context.Response.Headers["Cache-Control"]);
        }

        [Fact]
        public async Task SSEAddsAppropriateFraming()
        {
            var channel = Channel.CreateUnbounded<Message>();
            var context = new DefaultHttpContext();
            var sse = new ServerSentEventsTransport(channel, new LoggerFactory());
            var ms = new MemoryStream();
            context.Response.Body = ms;

            await channel.Out.WriteAsync(new Message(
                ReadableBuffer.Create(Encoding.UTF8.GetBytes("Hello World")).Preserve(),
                Format.Text,
                endOfMessage: true));

            Assert.True(channel.Out.TryComplete());

            await sse.ProcessRequestAsync(context);

            var expected = "data: Hello World\n\n";
            Assert.Equal(expected, Encoding.UTF8.GetString(ms.ToArray()));
        }

        [Fact]
        public async Task TransportEndsWhenRequestAborted()
        {
            var cts = new CancellationTokenSource();
            var channel = Channel.CreateUnbounded<Message>();
            var context = new DefaultHttpContext();
            context.RequestAborted = cts.Token;
            var poll = new ServerSentEventsTransport(channel, new LoggerFactory());

            var transportTask = poll.ProcessRequestAsync(context);

            cts.Cancel();

            await transportTask.WithTimeout();
        }

        [Fact]
        public async Task TransportEndsWhenChannelCompletedWithError()
        {
            var channel = Channel.CreateUnbounded<Message>();
            var context = new DefaultHttpContext();
            var poll = new ServerSentEventsTransport(channel, new LoggerFactory());

            var transportTask = poll.ProcessRequestAsync(context);

            var expected = new InvalidOperationException("Failed!");
            channel.Out.Complete(expected);

            var actual = await Assert.ThrowsAsync<InvalidOperationException>(async () => await transportTask.WithTimeout());
            Assert.Equal(expected.Message, actual.Message);
        }
    }
}
