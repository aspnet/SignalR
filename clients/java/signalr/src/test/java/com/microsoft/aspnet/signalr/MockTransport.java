// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

package com.microsoft.aspnet.signalr;

import java.util.ArrayList;

import io.reactivex.Observable;

class MockTransport implements Transport {
    private OnReceiveCallBack onReceiveCallBack;
    private ArrayList<String> sentMessages = new ArrayList<>();
    private String url;

    @Override
    public Observable<Void> start(String url) {
        this.url = url;
        return Observable.empty();
    }

    @Override
    public Observable<Void> send(String message) {
        sentMessages.add(message);
        return Observable.empty();
    }

    @Override
    public void setOnReceive(OnReceiveCallBack callback) {
        this.onReceiveCallBack = callback;
    }

    @Override
    public void onReceive(String message) throws Exception {
        this.onReceiveCallBack.invoke(message);
    }

    @Override
    public Observable<Void> stop() {
        return Observable.empty();
    }

    public void receiveMessage(String message) throws Exception {
        this.onReceive(message);
    }

    public String[] getSentMessages() {
        return sentMessages.toArray(new String[sentMessages.size()]);
    }

    public String getUrl() {
        return this.url;
    }
}
