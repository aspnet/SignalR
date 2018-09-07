// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

package com.microsoft.aspnet.signalr;

import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;
import java.util.concurrent.ConcurrentHashMap;

class CallbackMap {
    private ConcurrentHashMap<String, List<Binder>> handlers = new ConcurrentHashMap<>();

    public Binder put(String target, ActionBase action, ArrayList<Class<?>> classes) {
        Binder binder = new Binder(action, classes);
        handlers.computeIfPresent(target, (methodName, handlerList) -> {
            handlerList.add(binder);
            return handlerList;
        });
        handlers.computeIfAbsent(target, (ac) -> new ArrayList<>(Arrays.asList(binder)));
        return binder;
    }

    public Boolean containsKey(String key) {
        return handlers.containsKey(key);
    }

    public List<Binder> get(String key) {
        return handlers.get(key);
    }

    public void remove(String key) {
        handlers.remove(key);
    }
}
