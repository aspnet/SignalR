using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Builder.Internal;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Microsoft.AspNetCore.Sockets.HttpServer
{
    public static class HttpServerSocketBuilderExtensions
    {
        private static long _lastFrameConnectionId = long.MinValue;

        public static ISocketBuilder UseHttpServer(this ISocketBuilder socketBuilder, RequestDelegate requestDelegate)
        {
            return socketBuilder.UseHttpServer(app =>
            {
                app.Run(requestDelegate);
            });
        }

        public static ISocketBuilder UseHttpServer(this ISocketBuilder socketBuilder, Action<IApplicationBuilder> configure)
        {
            var trace = new KestrelTrace(null);
            var serviceContext = new ServiceContext
            {
                ConnectionManager = new FrameConnectionManager(trace, normalConnectionLimit:null, upgradedConnectionLimit:null),
                DateHeaderValueManager = new DateHeaderValueManager(),
                HttpParserFactory = context => new HttpParser<FrameAdapter>(showErrorDetails: true),
                Log = trace,
                SystemClock = new SystemClock(),
                ThreadPool = new LoggingThreadPool(trace),
            };

            return socketBuilder.Run(connection =>
            {
                var connectionId = CorrelationIdGenerator.GetNextId();
                var frameConnectionId = Interlocked.Increment(ref _lastFrameConnectionId);

                var frameConnection = new FrameConnection(new FrameConnectionContext
                {
                    ConnectionId = connectionId,
                    FrameConnectionId = frameConnectionId,
                    ServiceContext = serviceContext,
                    Connection = connection
                });

                var builder = new ApplicationBuilder(socketBuilder.ApplicationServices);
                configure(builder);
                var requestDelegate = builder.Build();
                var app = new HttpContextApplication(requestDelegate);
                return frameConnection.ProcessRequestsAsync(app);
            });
        }

        private class HttpContextApplication : IHttpApplication<HttpContext>
        {
            private readonly RequestDelegate _requestDelegate;

            public HttpContextApplication(RequestDelegate requestDelegate)
            {
                _requestDelegate = requestDelegate;
            }

            public HttpContext CreateContext(IFeatureCollection contextFeatures)
            {
                return new DefaultHttpContext(contextFeatures);
            }

            public void DisposeContext(HttpContext context, Exception exception)
            {

            }

            public Task ProcessRequestAsync(HttpContext context)
            {
                return _requestDelegate(context);
            }
        }
    }
}
