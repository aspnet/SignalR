// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.


namespace Microsoft.AspNetCore.SignalR.Internal.Protocol
{
    public class ResultMessage : HubMessage
    {
        public object Result { get; }

        public ResultMessage(string invocationId, object result) : base(invocationId)
        {
            Result = result;
        }

        public override string ToString()
        {
            return $"Result {{ {nameof(InvocationId)}: \"{InvocationId}\", {nameof(Result)}: {Result ?? "<<null>>"} ] }}";
        }
    }
}
