// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.AspNetCore.SignalR.Tests.Common;

namespace Microsoft.AspNetCore.SignalR.Redis.Tests
{
    public class RedisServerFixture<TStartup> : ServerFixture<TStartup>
        where TStartup : class
    {
        public RedisServerFixture() : base()
        {
            if (Docker.Default == null)
            {
                return;
            }

            Docker.Default.Start(_logger);
        }

        public override void Dispose()
        {
            if (Docker.Default != null)
            {
                Docker.Default.Stop(_logger);
            }

            base.Dispose();
        }
    }
}