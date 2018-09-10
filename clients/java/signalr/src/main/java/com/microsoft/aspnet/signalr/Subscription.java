// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

package com.microsoft.aspnet.signalr;

import java.util.List;

public class Subscription {
    private CallbackMap handlers;
    private InvocationHandler binder;
    private String target;

    public Subscription(CallbackMap handlers, InvocationHandler binder, String target) {
        this.handlers = handlers;
        this.binder = binder;
        this.target = target;
    }

    public void unsubscribe() {
        List<InvocationHandler> Binders = this.handlers.get(target);
        if (Binders != null) {
            Binders.remove(binder);
        }
    }
}
