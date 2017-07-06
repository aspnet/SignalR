using System;
using System.Collections.Generic;

namespace Microsoft.AspNetCore.Sockets.Features
{
    public interface IConnectionMetadataFeature
    {
        IDictionary<object, object> Metadata { get; set; }
    }
}
