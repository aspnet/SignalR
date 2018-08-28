package com.microsoft.aspnet.signalr;

import com.google.gson.JsonObject;
import org.apache.http.Header;
import org.apache.http.HttpRequest;
import org.apache.http.HttpResponse;
import org.apache.http.client.HttpClient;
import org.apache.http.client.methods.HttpPost;
import org.apache.http.client.methods.RequestBuilder;
import org.apache.http.impl.client.HttpClientBuilder;

import java.io.BufferedReader;
import java.io.IOException;
import java.io.InputStreamReader;

public class Negotiate {

    public static NegotiateResponse processNegotiate(String url) throws IOException {
        HttpClient client = HttpClientBuilder.create().build();
        HttpPost post = new HttpPost(url + "/negotiate");
        HttpResponse response = client.execute(post);

        BufferedReader rd = new BufferedReader(
                new InputStreamReader(response.getEntity().getContent()));

        StringBuffer result = new StringBuffer();
        String line = "";
        while ((line = rd.readLine()) != null) {
            result.append(line);
        }

        return new NegotiateResponse(result.toString());
    }

    public static NegotiateResponse processNegotiate(String url, String accessTokenHeader) throws IOException {
        HttpClient client = HttpClientBuilder.create().build();
        url = url.substring(0, url.indexOf('?')) + "negotiate" + url.substring(url.indexOf('?'));
        HttpPost post = new HttpPost(url);
        post.setHeader("Authorization", "Bearer " + accessTokenHeader);
        HttpResponse response = client.execute(post);

        BufferedReader rd = new BufferedReader(
                new InputStreamReader(response.getEntity().getContent()));

        StringBuffer result = new StringBuffer();
        String line = "";
        while ((line = rd.readLine()) != null) {
            result.append(line);
        }

        return new NegotiateResponse(result.toString());
    }
}
