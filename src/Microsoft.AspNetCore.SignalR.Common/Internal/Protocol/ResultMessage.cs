// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Microsoft.AspNetCore.SignalR.Internal.Protocol
{
    public class ResultMessage : HubMessage
    {
        public object Payload { get; }
        public string Error { get; }

        public ResultMessage(string invocationId, string error, object payload) : base(invocationId)
        {
            if (error != null && payload != null)
            {
                throw new ArgumentException($"Expected either '{nameof(error)}' or '{nameof(payload)}' to be provided, but not both");
            }

            Payload = payload;
            Error = error;
        }

        public override string ToString()
        {
            return $"Result {{ id: \"{InvocationId}\", error: \"{Error}\", payload: {Payload} ] }}";
        }

        // Static factory methods. Don't want to use constructor overloading because it will break down
        // if you need to send a payload statically-typed as a string. And because a static factory is clearer here
        public static ResultMessage WithError(string invocationId, string error) => new ResultMessage(invocationId, error, payload: null);

        public static ResultMessage WithPayload(string invocationId, object payload) => new ResultMessage(invocationId, error: null, payload: payload);
    }
}
