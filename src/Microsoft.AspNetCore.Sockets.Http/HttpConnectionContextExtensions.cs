using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace Microsoft.AspNetCore.Sockets
{
    public static class HttpConnectionContextExtensions
    {
        public static HttpContext GetHttpContext(this ConnectionContext connection)
        {
            return connection.Metadata.Get<HttpContext>(ConnectionMetadataNames.HttpContext);
        }
    }
}
