using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR.Internal;
using Microsoft.Extensions.Internal;

namespace Microsoft.AspNetCore.SignalR
{
    public class AllHubMethods
    {
        private static readonly Dictionary<Type, Dictionary<string, HubMethodDescriptor>> _methods = new Dictionary<Type, Dictionary<string, HubMethodDescriptor>>();

        internal static Dictionary<string, HubMethodDescriptor> DiscoverHubMethods<THub>()
        {
            var hubType = typeof(THub);

            return DiscoverHubMethods(hubType);
        }

        internal static Dictionary<string, HubMethodDescriptor> DiscoverHubMethods(Type hubType)
        {
            if (_methods.ContainsKey(hubType))
            {
                return _methods[hubType];
            }

            var hubMethods = new Dictionary<string, HubMethodDescriptor>(StringComparer.OrdinalIgnoreCase);

            var hubTypeInfo = hubType.GetTypeInfo();
            var hubName = hubType.Name;

            foreach (var methodInfo in HubReflectionHelper.GetHubMethods(hubType))
            {
                var methodName =
                    methodInfo.GetCustomAttribute<HubMethodNameAttribute>()?.Name ??
                    methodInfo.Name;

                if (hubMethods.ContainsKey(methodName))
                {
                    throw new NotSupportedException($"Duplicate definitions of '{methodName}'. Overloading is not supported.");
                }

                var executor = ObjectMethodExecutor.Create(methodInfo, hubTypeInfo);
                var authorizeAttributes = methodInfo.GetCustomAttributes<AuthorizeAttribute>(inherit: true);
                hubMethods[methodName] = new HubMethodDescriptor(executor, authorizeAttributes);
            }
            _methods.Add(hubType, hubMethods);

            return hubMethods;
        }
    }
}
