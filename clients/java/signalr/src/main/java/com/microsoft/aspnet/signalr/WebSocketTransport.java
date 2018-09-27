// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

package com.microsoft.aspnet.signalr;

import java.net.URI;
import java.net.URISyntaxException;
import java.util.Map;
import java.util.concurrent.CompletableFuture;

import okhttp3.*;

class WebSocketTransport implements Transport {
    private WebSocket newWebSocketClient;
    private SignalRWebSocketListener  webSocketListener = new SignalRWebSocketListener();
    private OnReceiveCallBack onReceiveCallBack;
    private URI url;
    private Logger logger;
    private Map<String, String> headers;
    private OkHttpClient httpClient;
    private CompletableFuture<Void> startFuture = new CompletableFuture<>();

    private static final String HTTP = "http";
    private static final String HTTPS = "https";
    private static final String WS = "ws";
    private static final String WSS = "wss";

    public WebSocketTransport(String url, Logger logger, Map<String, String> headers) throws URISyntaxException {
        this.url = formatUrl(url);
        this.logger = logger;
        this.headers = headers;
    }

    public WebSocketTransport(String url, Logger logger, Map<String, String> headers, OkHttpClient httpClient) throws URISyntaxException {
        this.url = formatUrl(url);
        this.logger = logger;
        this.headers = headers;
        this.httpClient = httpClient;
    }

    public WebSocketTransport(String url, Logger logger) throws URISyntaxException {
        this(url, logger, null);
    }

    public URI getUrl() {
        return url;
    }

    private URI formatUrl(String url) throws URISyntaxException {
        if (url.startsWith(HTTPS)) {
            url = WSS + url.substring(HTTPS.length());
        } else if (url.startsWith(HTTP)) {
            url = WS + url.substring(HTTP.length());
        }

        return new URI(url);
    }

    @Override
    public CompletableFuture start() {
        return CompletableFuture.runAsync(() -> {
            System.out.println(headers);
            logger.log(LogLevel.Debug, "Starting Websocket connection.");
            newWebSocketClient = createUpdatedWebSocket(webSocketListener);
            System.out.println(newWebSocketClient.request().headers().names());
        });
    }

    @Override
    public CompletableFuture send(String message) {
        return CompletableFuture.runAsync(() -> newWebSocketClient.send(message));
    }

    @Override
    public void setOnReceive(OnReceiveCallBack callback) {
        this.onReceiveCallBack = callback;
        logger.log(LogLevel.Debug, "OnReceived callback has been set");
    }

    @Override
    public void onReceive(String message) throws Exception {
        this.onReceiveCallBack.invoke(message);
    }

    @Override
    public CompletableFuture stop() {
        return CompletableFuture.runAsync(() -> {
            newWebSocketClient.close(0, "HubConnection stopped.");
            logger.log(LogLevel.Information, "WebSocket connection stopped");
        });
    }

    private WebSocket createUpdatedWebSocket(WebSocketListener webSocketListener) {
        Headers.Builder headerBuilder = new Headers.Builder();
        for (String key: headers.keySet()) {
            headerBuilder.add(key, headers.get(key));
        }
        Request request = new Request.Builder().url(url.toString())
                .headers(headerBuilder.build())
                .build();
        System.out.println("Cookies: " + httpClient.cookieJar());
        return this.httpClient.newWebSocket(request, webSocketListener);
    }


    private class SignalRWebSocketListener extends WebSocketListener {
        @Override
        public void onOpen(WebSocket webSocket, Response response) {
            //startFuture = CompletableFuture.completedFuture(null);
            logger.log(LogLevel.Information, "WebSocket transport connected to: %s", newWebSocketClient.request().url());
        }

        @Override
        public void onMessage(WebSocket webSocket, String message) {
            try {
                onReceive(message);
            } catch (Exception e) {
                e.printStackTrace();
            }
        }

        @Override
        public void onClosing(WebSocket webSocket, int code, String reason) {
            logger.log(LogLevel.Information, "WebSocket connection stopping with " +
                    "code %d and reason %d", code, reason);
        }

        @Override
        public void onFailure(WebSocket webSocket, Throwable t, Response response) {
            logger.log(LogLevel.Error, "Error : " + t.getMessage());
//            if(!startFuture.isDone()){
//                String errorMessage = "There was an error starting the Websockets transport.";
//                logger.log(LogLevel.Debug, errorMessage);
//                startFuture.completeExceptionally(new RuntimeException(errorMessage));
//            }
        }
    }
}
