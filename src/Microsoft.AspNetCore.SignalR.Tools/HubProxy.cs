// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using Mono.Cecil;

namespace Microsoft.AspNetCore.SignalR.Tools
{
    public class HubProxy
    {
        public HubProxy(string name, string @namespace, IEnumerable<MethodDefinition> methods)
        {
            Name = name;
            Namespace = @namespace;
            Methods = methods;
        }

        public string Name { get; private set; }

        public string Namespace { get; private set; }

        public IEnumerable<MethodDefinition> Methods { get; private set; }
    }
}
