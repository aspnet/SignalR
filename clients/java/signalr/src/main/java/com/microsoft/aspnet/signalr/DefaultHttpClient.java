// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

package com.microsoft.aspnet.signalr;

import java.io.IOException;
import java.util.concurrent.CompletableFuture;

import okhttp3.Call;
import okhttp3.Callback;
import okhttp3.OkHttpClient;
import okhttp3.Request;
import okhttp3.RequestBody;
import okhttp3.Response;
import okhttp3.ResponseBody;

class DefaultHttpClient extends HttpClient {
    private OkHttpClient client = new OkHttpClient();

    @Override
    public CompletableFuture<HttpResponse> send(HttpRequest httpRequest) {
        Request.Builder requestBuilder = new Request.Builder().url(httpRequest.getUrl());
        if (httpRequest.getMethod() == "GET") {
            requestBuilder.get();
        } else if (httpRequest.getMethod() == "POST") {
            RequestBody body = RequestBody.create(null, new byte[] {});
            requestBuilder.post(body);
        } else if (httpRequest.getMethod() == "DELETE") {
            requestBuilder.delete();
        }

        if (httpRequest.getHeaders() != null) {
            httpRequest.getHeaders().forEach((key, value) -> {
                requestBuilder.addHeader(key, value);
            });
        }

        Request request = requestBuilder.build();

        CompletableFuture<HttpResponse> responseFuture = new CompletableFuture<>();

        client.newCall(request).enqueue(new Callback() {
            @Override
            public void onFailure(Call call, IOException e) {
                responseFuture.completeExceptionally(e.getCause());
            }

            @Override
            public void onResponse(Call call, Response response) throws IOException {
                try (ResponseBody body = response.body()) {
                    HttpResponse httpResponse = new HttpResponse(response.code(), response.message(), body.string());
                    responseFuture.complete(httpResponse);
                }
            }
        });

        return responseFuture;
    }
}