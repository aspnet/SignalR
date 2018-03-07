// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Linq;
using System.Net.Http.Headers;
using System.Reflection;

namespace Microsoft.AspNetCore.Sockets.Client.Http
{
    public static class Constants
    {
        public static readonly ProductInfoHeaderValue UserAgentHeader;

        static Constants()
        {
            var assemblyVersion = (AssemblyInformationalVersionAttribute)typeof(Constants)
                .Assembly
                .GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute))
                .Single();

            var userAgent = "Microsoft.AspNetCore.Sockets.Client.Http/" + assemblyVersion.InformationalVersion;
            UserAgentHeader = ProductInfoHeaderValue.Parse(userAgent);
        }
    }
}
