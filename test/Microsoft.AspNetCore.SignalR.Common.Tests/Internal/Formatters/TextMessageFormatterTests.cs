﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Text;
using Microsoft.AspNetCore.SignalR.Internal.Formatters;
using Xunit;

namespace Microsoft.AspNetCore.SignalR.Common.Tests.Internal.Formatters
{
    public class TextMessageFormatterTests
    {
        [Fact]
        public void WriteMessage()
        {
            using (var ms = new MemoryStream())
            {
                TextMessageFormatter.WriteMessage(new ReadOnlySpan<byte>(Encoding.UTF8.GetBytes("ABC")), ms);
                Assert.Equal("ABC\u001e", Encoding.UTF8.GetString(ms.ToArray()));
            }
        }
    }
}
