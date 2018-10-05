// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

package com.microsoft.aspnet.signalr;

import java.util.*;
import java.util.concurrent.ConcurrentHashMap;

class CallbackMap {
    private Map<String, List<InvocationHandler>> handlers = new ConcurrentHashMap<>();

    public InvocationHandler put(String target, ActionBase action, Class<?>... classes) {
        InvocationHandler handler = new InvocationHandler(action, classes);
        handlers.computeIfAbsent(target, ac -> new ArrayList<>()).add(handler);
        return handler;
    }

    public List<InvocationHandler> get(String key) {
        return handlers.get(key);
    }

    public void remove(String key) {
        handlers.remove(key);
    }
}
