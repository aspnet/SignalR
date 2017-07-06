using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace Microsoft.AspNetCore.Sockets.Http.Features
{
    public interface IHttpContextFeature
    {
        HttpContext HttpContext { get; set; }
    }

    public class HttpContextFeature : IHttpContextFeature
    {
        public HttpContext HttpContext { get; set; }
    }
}
