using System;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.Sockets
{
    public static class SocketBuilderExtensions
    {
        public static ISocketBuilder Use(this ISocketBuilder socketBuilder, Func<ConnectionContext, Func<Task>, Task> middleware)
        {
            return socketBuilder.Use(next =>
            {
                return context =>
                {
                    Func<Task> simpleNext = () => next(context);
                    return middleware(context, simpleNext);
                };
            });
        }
    }
}
