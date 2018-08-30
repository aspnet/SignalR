// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

package com.microsoft.aspnet.signalr;

import okhttp3.OkHttpClient;
import okhttp3.Request;
import okhttp3.RequestBody;
import okhttp3.Response;
import org.apache.http.HttpResponse;

import java.io.BufferedReader;
import java.io.IOException;
import java.io.InputStreamReader;
import java.net.URISyntaxException;

public class Negotiate {

    public static NegotiateResponse processNegotiate(String url) throws IOException {
        url = resolveNegotiateUrl(url);
        OkHttpClient client = new OkHttpClient();
        RequestBody body = RequestBody.create(null, new byte[]{});
        Request request =  new Request.Builder()
                .url(url)
                .post(body)
                .build();
        Response response = client.newCall(request).execute();
        String result = response.body().string();
        return new NegotiateResponse(result);
    }

    public static NegotiateResponse processNegotiate(String url, String accessTokenHeader) throws IOException, URISyntaxException {
        url = resolveNegotiateUrl(url);
        OkHttpClient client = new OkHttpClient();
        RequestBody body = RequestBody.create(null, new byte[]{});
        Request request =  new Request.Builder()
                .url(url)
                .addHeader("Authorization", "Bearer " + accessTokenHeader)
                .post(body)
                .build();
        Response response = client.newCall(request).execute();
        String result = response.body().string();
        return new NegotiateResponse(result);
    }

    private static String getResponseResult(HttpResponse response) throws IOException {
        BufferedReader rd = new BufferedReader(
                new InputStreamReader(response.getEntity().getContent()));

        StringBuffer result = new StringBuffer();
        String line = "";
        while ((line = rd.readLine()) != null) {
            result.append(line);
        }

        return result.toString();
    }

    private static String resolveNegotiateUrl(String url){
        String negotiateUrl = "";

        // Check if we have a query string. If we do then we ignore it for now.
        int queryStringIndex = url.indexOf('?');
        if(queryStringIndex > 0){
            negotiateUrl = url.substring(0, url.indexOf('?'));
        }
        else {
            negotiateUrl = url;
        }

        //Check if the url ends in a /
        if(negotiateUrl.charAt(negotiateUrl.length() -1) != '/'){
            negotiateUrl += "/";
        }

        negotiateUrl += "negotiate";

        // Add the query string back if it existed.
        if(queryStringIndex > 0){
            negotiateUrl += url.substring(url.indexOf('?'));
        }

        return negotiateUrl;
    }
}
