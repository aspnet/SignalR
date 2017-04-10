// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Linq;

namespace Microsoft.AspNetCore.SignalR.Internal.Protocol
{
    public class InvocationMessage : HubMessage
    {
        public string Target { get; }

        public object[] Arguments { get; }

        public InvocationMessage(string invocationId, string target, object[] arguments) : base(invocationId)
        {
            Target = target;
            Arguments = arguments;
        }

        public override string ToString()
        {
            return $"Invocation {{ {nameof(InvocationId)}: \"{InvocationId}\", {nameof(Target)}: \"{Target}\", {nameof(Arguments)}: [ {string.Join(", ", Arguments.Select(a => a.ToString()))} ] }}";
        }
    }
}
