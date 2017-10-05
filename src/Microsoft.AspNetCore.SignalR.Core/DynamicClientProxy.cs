﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Dynamic;

namespace Microsoft.AspNetCore.SignalR
{ 
    public class DynamicClientProxy : DynamicObject
    {
        private readonly IClientProxy _clientProxy;

        public DynamicClientProxy(IClientProxy clientProxy)
        {
            _clientProxy = clientProxy;
        }

        public override bool TryInvokeMember(InvokeMemberBinder binder, object[] args, out object result)
        {
            result = _clientProxy.InvokeAsync(binder.Name, args);
            return true;
        }
    }
}
