﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Microsoft.AspNetCore.SignalR.Internal.Protocol
{
    public class CompletionMessage : HubMessage
    {
        public string Error { get; }
        public object Result { get; }
        public bool HasResult { get; }

        public CompletionMessage(string invocationId, string error, object result, bool hasResult) : base(invocationId)
        {
            if (error != null && result != null)
            {
                throw new ArgumentException($"Expected either '{nameof(error)}' or '{nameof(result)}' to be provided, but not both");
            }
            Error = error;
            Result = result;
        }

        public override string ToString()
        {
            var errorStr = Error == null ? "<<null>>" : $"\"{Error}\"";
            return $"Completion {{ {nameof(InvocationId)}: \"{InvocationId}\", {nameof(Error)}: {errorStr}, {nameof(Result)}: {Result ?? "<<null>>"} }}";
        }

        // Static factory methods. Don't want to use constructor overloading because it will break down
        // if you need to send a payload statically-typed as a string. And because a static factory is clearer here
        public static CompletionMessage Empty(string invocationId) => new CompletionMessage(invocationId, error: null, result: null, hasResult: false);

        public static CompletionMessage WithError(string invocationId, string error) => new CompletionMessage(invocationId, error, result: null, hasResult: false);

        public static CompletionMessage WithResult(string invocationId, object payload) => new CompletionMessage(invocationId, error: null, result: payload, hasResult: true);
    }
}
