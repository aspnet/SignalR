// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

package com.microsoft.aspnet.signalr;

import java.util.Map;
import java.util.concurrent.CompletableFuture;
import java.util.function.BiConsumer;

import okhttp3.Headers;
import okhttp3.OkHttpClient;
import okhttp3.Request;
import okhttp3.Response;
import okhttp3.WebSocket;
import okhttp3.WebSocketListener;

class OkHttpWebSocketWrapper extends WebSocketWrapper {
    private WebSocket websocketClient;
    private String url;
    private Map<String, String> headers;
    private OkHttpClient client;
    private Logger logger;
    private OnReceiveCallBack onReceive;
    private BiConsumer<Integer, String> onClose;
    private CompletableFuture<Void> startFuture = new CompletableFuture<>();
    private CompletableFuture<Void> closeFuture = new CompletableFuture<>();

    public OkHttpWebSocketWrapper(String url, Map<String, String> headers, OkHttpClient client, Logger logger) {
        this.url = url;
        this.headers = headers;
        this.client = client;
        this.logger = logger;
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

        this.websocketClient = client.newWebSocket(request, new SignalRWebSocketListener());
        return startFuture;
    }

    @Override
    public CompletableFuture<Void> stop() {
        websocketClient.close(1000, "HubConnection stopped.");
        return closeFuture;
    }

    @Override
    public CompletableFuture<Void> send(String message) {
        return CompletableFuture.runAsync(() -> websocketClient.send(message));
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
            logger.log(LogLevel.Information, "WebSocket transport connected to: %s", websocketClient.request().url());
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
            closeFuture.complete(null);
            checkStartFailure();
        }

        @Override
        public void onFailure(WebSocket webSocket, Throwable t, Response response) {
            logger.log(LogLevel.Error, "Error : %s", t.getMessage());
            closeFuture.completeExceptionally(new RuntimeException());
            checkStartFailure();
        }

        private void checkStartFailure() {
            // If the start future hasn't completed yet, then we need to complete it
            // exceptionally.
            if (!startFuture.isDone()) {
                String errorMessage = "There was an error starting the Websockets transport.";
                logger.log(LogLevel.Debug, errorMessage);
                startFuture.completeExceptionally(new RuntimeException(errorMessage));
            }
        }
    }
}