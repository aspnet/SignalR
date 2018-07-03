using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Channels;

namespace Microsoft.AspNetCore.SignalR
{
    internal static class ReflectionHelper
    {
        public static bool IsStreamingType(Type type)
        {
            // IMPORTANT !!
            // All valid types must be generic
            // because HubConnectionContext gets the generic arguement and uses it to determine the expected item type of the stream
            // The long-term solution is making a (streaming type => expected item type) method.

            if (!type.IsGenericType)
            {
                return false;
            }

            // walk up inheritance chain, until parent is either null or a ChannelReader<T>
            // TODO -- add Streams here, to make sending files ez
            while (true)
            {
                if (type == null)
                {
                    return false;
                }

                if (type.GetGenericTypeDefinition() == typeof(ChannelReader<>))
                {
                    return true;
                }

                type = type.BaseType;
            }
        }
    }
}
