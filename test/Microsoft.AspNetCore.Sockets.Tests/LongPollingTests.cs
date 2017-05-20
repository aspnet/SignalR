﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.IO.Pipelines;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks.Channels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Sockets.Internal.Formatters;
using Microsoft.AspNetCore.Sockets.Transports;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Microsoft.AspNetCore.Sockets.Tests
{
    public class LongPollingTests
    {
        [Fact]
        public async Task Set204StatusCodeWhenChannelComplete()
        {
            var channel = Channel.CreateUnbounded<Message>();
            var context = new DefaultHttpContext();
            var poll = new LongPollingTransport(channel, new LoggerFactory());

            Assert.True(channel.Out.TryComplete());

            await poll.ProcessRequestAsync(context, context.RequestAborted);

            Assert.Equal(204, context.Response.StatusCode);
        }

        [Fact]
        public async Task FrameSentAsSingleResponse()
        {
            var channel = Channel.CreateUnbounded<Message>();
            var context = new DefaultHttpContext();
            var poll = new LongPollingTransport(channel, new LoggerFactory());
            var ms = new MemoryStream();
            context.Response.Body = ms;

            await channel.Out.WriteAsync(new Message(
                Encoding.UTF8.GetBytes("Hello World"),
                MessageType.Text,
                endOfMessage: true));

            Assert.True(channel.Out.TryComplete());

            await poll.ProcessRequestAsync(context, context.RequestAborted);

            Assert.Equal(200, context.Response.StatusCode);
            Assert.Equal("T11:T:Hello World;", Encoding.UTF8.GetString(ms.ToArray()));
        }

        [Theory]
        [InlineData(MessageFormat.Text, "T5:T:Hello;1:T: ;5:T:World;")]
        [InlineData(MessageFormat.Binary, "QgAAAAAAAAAFAEhlbGxvAAAAAAAAAAEAIAAAAAAAAAAFAFdvcmxk")]
        public async Task MultipleFramesSentAsSingleResponse(MessageFormat format, string expectedPayload)
        {
            var channel = Channel.CreateUnbounded<Message>();
            var context = new DefaultHttpContext();
            if (format == MessageFormat.Binary)
            {
                context.Request.Headers["Accept"] = MessageFormatter.BinaryContentType;
            }
            var poll = new LongPollingTransport(channel, new LoggerFactory());
            var ms = new MemoryStream();
            context.Response.Body = ms;

            await channel.Out.WriteAsync(new Message(
                Encoding.UTF8.GetBytes("Hello"),
                MessageType.Text,
                endOfMessage: true));
            await channel.Out.WriteAsync(new Message(
                Encoding.UTF8.GetBytes(" "),
                MessageType.Text,
                endOfMessage: true));
            await channel.Out.WriteAsync(new Message(
                Encoding.UTF8.GetBytes("World"),
                MessageType.Text,
                endOfMessage: true));

            Assert.True(channel.Out.TryComplete());

            await poll.ProcessRequestAsync(context, context.RequestAborted);

            Assert.Equal(200, context.Response.StatusCode);

            var payload = ms.ToArray();
            var encoded = format == MessageFormat.Binary ?
                Convert.ToBase64String(payload) :
                Encoding.UTF8.GetString(payload);
            Assert.Equal(expectedPayload, encoded);
        }
    }
}
