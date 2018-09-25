// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

package com.microsoft.aspnet.signalr;

import java.util.HashMap;
import java.util.Map;
import java.util.concurrent.CompletableFuture;

class HttpRequest {
    String method;

    String url;

    //byte[] | String content;

    Map<String, String> headers = new HashMap<>();

    //responseType?:??;

    //abortSignal?:AbortSignal;

    //timeout?:number;
}

class HttpResponse {
    private Integer statusCode;
    private String statusText;
    private String content = null;

    public HttpResponse(Integer statusCode) {
        this.statusCode = statusCode;
    }

    public HttpResponse(Integer statusCode, String statusText) {
        this.statusCode = statusCode;
        this.statusText = statusText;
    }

    public HttpResponse(Integer statusCode, String statusText, String content) {
        this.statusCode = statusCode;
        this.statusText = statusText;
        this.content = content;
    }

    public String getContent() {
        return content;
    }
}

abstract class HttpClient {
    public CompletableFuture<HttpResponse> get(String url) {
        HttpRequest request = new HttpRequest();
        request.url = url;
        request.method = "GET";
        return this.send(request);
    }

    public CompletableFuture<HttpResponse> get(String url, HttpRequest options) {
        options.url = url;
        options.method = "GET";
        return this.send(options);
    }

    public CompletableFuture<HttpResponse> post(String url) {
        HttpRequest request = new HttpRequest();
        request.url = url;
        request.method = "POST";
        return this.send(request);
    }

    public CompletableFuture<HttpResponse> post(String url, HttpRequest options) {
        options.url = url;
        options.method = "POST";
        return this.send(options);
    }

    public CompletableFuture<HttpResponse> delete(String url) {
        HttpRequest request = new HttpRequest();
        request.url = url;
        request.method = "DELETE";
        return this.send(request);
    }
    
    public CompletableFuture<HttpResponse> delete(String url, HttpRequest options) {
        options.url = url;
        options.method = "DELETE";
        return this.send(options);
    }

    public abstract CompletableFuture<HttpResponse> send(HttpRequest request);
}