using System;
using System.Collections.Generic;

namespace Microsoft.AspNetCore.SignalR.Internal
{
    public static class TypeBaseEnumerationExtensions
    {
        public static IEnumerable<Type> AllBaseTypes(this Type type)
        {
            var current = type;
            while (current != null)
            {
                yield return current;
                current = current.BaseType;
            }
        }
    }
}
