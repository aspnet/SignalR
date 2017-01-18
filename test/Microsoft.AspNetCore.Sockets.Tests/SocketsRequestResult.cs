using Microsoft.AspNetCore.Http;

namespace Microsoft.AspNetCore.Sockets.Tests
{
    public class SocketsRequestResult
    {
        public byte[] ResponseBody { get; }
        public DefaultHttpContext HttpContext { get; }

        public SocketsRequestResult(DefaultHttpContext httpContext, byte[] responseBody)
        {
            HttpContext = httpContext;
            ResponseBody = responseBody;
        }
    }
}