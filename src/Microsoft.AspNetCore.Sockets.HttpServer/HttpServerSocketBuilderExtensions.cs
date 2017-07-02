using System;
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
        public static ISocketBuilder UseHttpServer(this ISocketBuilder socketBuilder, RequestDelegate requestDelegate)
        {
            return socketBuilder.UseHttpServer(app =>
            {
                app.Run(requestDelegate);
            });
        }

        public static ISocketBuilder UseHttpServer(this ISocketBuilder socketBuilder, Action<IApplicationBuilder> configure)
        {
            return socketBuilder.Run(connection =>
            {
                var builder = new ApplicationBuilder(socketBuilder.ApplicationServices);
                configure(builder);
                var requestDelegate = builder.Build();
                var app = new HttpContextApplication(requestDelegate);
                var frame = new Frame<HttpContext>(app, new FrameContext
                {
                    ConnectionId = connection.ConnectionId,
                    Input = connection.Transport.Reader,
                    Output = connection.Transport.Writer,
                    Connection = connection,
                    ServiceContext = new ServiceContext()
                    {

                    },
                });

                return frame.ProcessRequestsAsync();
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
