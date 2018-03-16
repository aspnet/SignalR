// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.IO;
using System.IO.Pipelines;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Sockets.Internal.Transports;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Microsoft.AspNetCore.Sockets.Tests
{
    public class ServerSentEventsTests
    {
        [Fact]
        public async Task SSESetsContentType()
        {
            var pair = DuplexPipe.CreateConnectionPair(PipeOptions.Default, PipeOptions.Default);
            var connection = new DefaultConnectionContext("foo", pair.Transport, pair.Application);
            var context = new DefaultHttpContext();

            var sse = new ServerSentEventsTransport(connection.Application.Input, connectionId: string.Empty, loggerFactory: new LoggerFactory());

            connection.Transport.Output.Complete();

            await sse.ProcessRequestAsync(context, context.RequestAborted);

            Assert.Equal("text/event-stream", context.Response.ContentType);
            Assert.Equal("no-cache", context.Response.Headers["Cache-Control"]);
        }

        [Fact]
        public async Task SSETurnsResponseBufferingOff()
        {
            var pair = DuplexPipe.CreateConnectionPair(PipeOptions.Default, PipeOptions.Default);
            var connection = new DefaultConnectionContext("foo", pair.Transport, pair.Application);
            var context = new DefaultHttpContext();

            var feature = new HttpBufferingFeature();
            context.Features.Set<IHttpBufferingFeature>(feature);
            var sse = new ServerSentEventsTransport(connection.Application.Input, connectionId: connection.ConnectionId, loggerFactory: new LoggerFactory());

            connection.Transport.Output.Complete();

            await sse.ProcessRequestAsync(context, context.RequestAborted);

            Assert.True(feature.ResponseBufferingDisabled);
        }

        [Fact]
        public async Task SSEWritesMessages()
        {
            var pair = DuplexPipe.CreateConnectionPair(PipeOptions.Default, new PipeOptions(readerScheduler: PipeScheduler.Inline));
            var connection = new DefaultConnectionContext("foo", pair.Transport, pair.Application);
            var context = new DefaultHttpContext();

            var ms = new MemoryStream();
            context.Response.Body = ms;
            var sse = new ServerSentEventsTransport(connection.Application.Input, connectionId: string.Empty, loggerFactory: new LoggerFactory());

            var task = sse.ProcessRequestAsync(context, context.RequestAborted);

            await connection.Transport.Output.WriteAsync(Encoding.ASCII.GetBytes("Hello"));
            connection.Transport.Output.Complete();
            await task.OrTimeout();
            Assert.Equal(":\r\ndata: Hello\r\n\r\n", Encoding.ASCII.GetString(ms.ToArray()));
        }

        [Theory]
        [InlineData("Hello World", ":\r\ndata: Hello World\r\n\r\n")]
        [InlineData("Hello\nWorld", ":\r\ndata: Hello\r\ndata: World\r\n\r\n")]
        [InlineData("Hello\r\nWorld", ":\r\ndata: Hello\r\ndata: World\r\n\r\n")]
        public async Task SSEAddsAppropriateFraming(string message, string expected)
        {
            var pair = DuplexPipe.CreateConnectionPair(PipeOptions.Default, PipeOptions.Default);
            var connection = new DefaultConnectionContext("foo", pair.Transport, pair.Application);
            var context = new DefaultHttpContext();

            var sse = new ServerSentEventsTransport(connection.Application.Input, connectionId: string.Empty, loggerFactory: new LoggerFactory());
            var ms = new MemoryStream();
            context.Response.Body = ms;

            await connection.Transport.Output.WriteAsync(Encoding.UTF8.GetBytes(message));

            connection.Transport.Output.Complete();

            await sse.ProcessRequestAsync(context, context.RequestAborted);

            Assert.Equal(expected, Encoding.UTF8.GetString(ms.ToArray()));
        }

        private class HttpBufferingFeature : IHttpBufferingFeature
        {
            public bool RequestBufferingDisabled { get; set; }

            public bool ResponseBufferingDisabled { get; set; }

            public void DisableRequestBuffering()
            {
                RequestBufferingDisabled = true;
            }

            public void DisableResponseBuffering()
            {
                ResponseBufferingDisabled = true;
            }
        }
    }
}
