using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.AspNetCore.SignalR
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public class HubMethodNameAttribute : Attribute
    {
        public string Name { get; }

        public HubMethodNameAttribute(string name)
        {
            Name = name;
        }
    }
}
