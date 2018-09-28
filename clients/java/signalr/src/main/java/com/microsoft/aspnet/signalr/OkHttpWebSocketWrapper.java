// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

package com.microsoft.aspnet.signalr;

import java.util.Map;
import java.util.concurrent.CompletableFuture;
import java.util.function.BiConsumer;
import java.util.function.BiFunction;
import java.util.function.Consumer;

import okhttp3.Headers;
import okhttp3.OkHttpClient;
import okhttp3.Request;
import okhttp3.Response;
import okhttp3.WebSocket;
import okhttp3.WebSocketListener;

class OkHttpWebSocketWrapper extends WebSocketWrapper {
    private WebSocket websocket;
    private String url;
    private Map<String, String> headers;
    private OkHttpClient client;
    private OnReceiveCallBack onReceive;
    private BiConsumer<Integer, String> onClose;
    private CompletableFuture<Void> startFuture = new CompletableFuture<>();
    private CompletableFuture<Void> closeFuture = new CompletableFuture<>();

    public OkHttpWebSocketWrapper(String url, Map<String, String> headers, OkHttpClient client) {
        this.url = url;
        this.headers = headers;
        this.client = client;
    }

    @Override
    public CompletableFuture<Void> start() {
        Headers.Builder headerBuilder = new Headers.Builder();
        for (String key : headers.keySet()) {
            headerBuilder.add(key, headers.get(key));
        }

        Request request = new Request.Builder()
            .url(url.toString())
            .headers(headerBuilder.build())
            .build();

        this.websocket = client.newWebSocket(request, new SignalRWebSocketListener());
        return startFuture;
    }

    @Override
    public CompletableFuture<Void> stop() {
        websocket.close(1000, "");
        return closeFuture;
    }

    @Override
    public CompletableFuture<Void> send(String message) {
        return CompletableFuture.runAsync(() -> websocket.send(message));
    }

    @Override
    public void setOnReceive(OnReceiveCallBack onReceive) {
        this.onReceive = onReceive;
    }

    @Override
    public void setOnClose(BiConsumer<Integer, String> onClose) {
        this.onClose = onClose;
    }

    private class SignalRWebSocketListener extends WebSocketListener {
        @Override
        public void onOpen(WebSocket webSocket, Response response) {
            startFuture.complete(null);
        }

        @Override
        public void onMessage(WebSocket webSocket, String message) {
            try {
                onReceive.invoke(message);
            } catch (Exception e) {
                // TODO Auto-generated catch block
                e.printStackTrace();
            }
        }

        @Override
        public void onClosing(WebSocket webSocket, int code, String reason) {
            onClose.accept(code, reason);
            startFuture.completeExceptionally(new RuntimeException());
            closeFuture.complete(null);
        }

        @Override
        public void onFailure(WebSocket webSocket, Throwable t, Response response) {
            startFuture.completeExceptionally(new RuntimeException());
            closeFuture.completeExceptionally(new RuntimeException());
        }
    }
}