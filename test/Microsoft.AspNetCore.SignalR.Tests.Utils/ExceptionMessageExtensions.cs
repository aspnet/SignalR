// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Microsoft.AspNetCore.SignalR.Tests
{
    public static class ExceptionMessageExtensions
    {
        public static string GetLocalizationSafeMessage(this ArgumentException argEx)
        {
            // Strip off the last line since it's "Parameter Name: [parameterName]" and:
            // 1. We verify the parameter name separately
            // 2. It is localized, so we don't want our tests to break in non-US environments
            var message = argEx.Message;
            var lastNewline = message.LastIndexOf(Environment.NewLine, StringComparison.Ordinal);
            if (lastNewline < 0)
            {
                return message;
            }

            return message.Substring(0, lastNewline);
        }
    }
}
