using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks.Channels;

namespace Microsoft.AspNetCore.Sockets.Features
{
    public interface IConnectionTransportFeature
    {
        Channel<byte[]> Transport { get; set; }
    }
}
