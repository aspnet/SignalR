using System;

namespace Microsoft.AspNetCore.SignalR
{
    [AttributeUsage(AttributeTargets.ReturnValue, AllowMultiple = false, Inherited = false)]
    public class StreamingAttribute : Attribute
    {
        // TODO: Allow specifying channel bounds and optimizations here?
    }
}
