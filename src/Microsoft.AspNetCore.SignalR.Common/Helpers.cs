using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Channels;

namespace Microsoft.AspNetCore.SignalR
{
    internal static class Helpers
    {
        public static bool ShouldBeTreatedAsStreamingParameter(Type type)
        {
            // walk up inheritance chain, until parent is either null or a ChannelReader<T>
            // TODO -- add Streams here, to make sending files ez
            while (true)
            {
                if (type == null)
                {
                    return false;
                }

                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ChannelReader<>))
                {
                    return true;
                }

                type = type.BaseType;
            }
        }
    }
}
