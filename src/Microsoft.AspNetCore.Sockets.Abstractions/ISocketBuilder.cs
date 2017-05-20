using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.AspNetCore.Sockets
{
    public interface ISocketBuilder
    {
        IServiceProvider ApplicationServices { get; }

        ISocketBuilder Use(Func<SocketDelegate, SocketDelegate> middleware);

        SocketDelegate Build();
    }
}
