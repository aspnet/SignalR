// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

package com.microsoft.signalr;

import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;

class CallbackMap {
    private final Map<String, List<InvocationHandler>> handlers = new ConcurrentHashMap<>();

    public InvocationHandler put(String target, ActionBase action, Class<?>... classes) {
        InvocationHandler handler = new InvocationHandler(action, classes);
        if (!handlers.containsKey(target)){
            handlers.put(target, new ArrayList<>());
        }
        handlers.get(target).add(handler);
        return handler;
    }

    public List<InvocationHandler> get(String key) {
        return handlers.get(key);
    }

    public void remove(String key) {
        handlers.remove(key);
    }
}
