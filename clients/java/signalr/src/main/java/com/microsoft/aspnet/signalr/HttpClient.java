// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

package com.microsoft.aspnet.signalr;

import java.util.HashMap;
import java.util.Map;
import java.util.concurrent.CompletableFuture;

import io.reactivex.Observable;

class HttpRequest {
    private String method;
    private String url;
    private Map<String, String> headers = new HashMap<>();

    public void setMethod(String method) {
        this.method = method;
    }

    public void setUrl(String url) {
        this.url = url;
    }

    public void setHeader(String key, String value) {
        this.headers.put(key, value);
    }

    public void setHeaders(Map<String, String> headers) {
        headers.forEach((key, value) -> {
            this.headers.put(key, value);
        });
    }

    public String getMethod() {
        return method;
    }

    public String getUrl() {
        return url;
    }

    public Map<String, String> getHeaders() {
        return headers;
    }
}

class HttpResponse {
    private int statusCode;
    private String statusText;
    private String content = null;

    public HttpResponse(int statusCode) {
        this.statusCode = statusCode;
    }

    public HttpResponse(int statusCode, String statusText) {
        this.statusCode = statusCode;
        this.statusText = statusText;
    }

    public HttpResponse(int statusCode, String statusText, String content) {
        this.statusCode = statusCode;
        this.statusText = statusText;
        this.content = content;
    }

    public String getContent() {
        return content;
    }

    public int getStatusCode() {
        return statusCode;
    }

    public String getStatusText() {
        return statusText;
    }
}

abstract class HttpClient {
    public Observable<HttpResponse> get(String url) {
        HttpRequest request = new HttpRequest();
        request.setUrl(url);
        request.setMethod("GET");
        return this.send(request);
    }

    public Observable<HttpResponse> get(String url, HttpRequest options) {
        options.setUrl(url);
        options.setMethod("GET");
        return this.send(options);
    }

    public Observable<HttpResponse> post(String url) {
        HttpRequest request = new HttpRequest();
        request.setUrl(url);
        request.setMethod("POST");
        return this.send(request);
    }

    public Observable<HttpResponse> post(String url, HttpRequest options) {
        options.setUrl(url);
        options.setMethod("POST");
        return this.send(options);
    }

    public Observable<HttpResponse> delete(String url) {
        HttpRequest request = new HttpRequest();
        request.setUrl(url);
        request.setMethod("DELETE");
        return this.send(request);
    }

    public Observable<HttpResponse> delete(String url, HttpRequest options) {
        options.setUrl(url);
        options.setMethod("DELETE");
        return this.send(options);
    }

    public abstract Observable<HttpResponse> send(HttpRequest request);

    public abstract WebSocketWrapper createWebSocket(String url, Map<String, String> headers);
}