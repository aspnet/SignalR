// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

package com.microsoft.aspnet.signalr;

import java.util.function.BiConsumer;

import io.reactivex.Observable;

abstract class WebSocketWrapper {
    public abstract Observable<Void> start();

    public abstract Observable<Void> stop();

    public abstract Observable<Void> send(String message);

    public abstract void setOnReceive(OnReceiveCallBack onReceive);

    public abstract void setOnClose(BiConsumer<Integer, String> onClose);
}