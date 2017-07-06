using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.AspNetCore.Sockets.Features
{
    public interface IConnectionIdFeature
    {
        string ConnectionId { get; set; }
    }
}
