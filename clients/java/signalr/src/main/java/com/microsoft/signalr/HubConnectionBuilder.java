// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

package com.microsoft.signalr;

import java.util.concurrent.CompletableFuture;
import java.util.function.Supplier;

public class HubConnectionBuilder {
    private String url;
    private Transport transport;
    private Logger logger;
    private HttpClient httpClient;
    private boolean skipNegotiate;
    private Supplier<CompletableFuture<String>> accessTokenProvider;

    private HubConnectionBuilder(String url) {
        this.url = url;
    }

    public static HubConnectionBuilder create(String url) {
        if (url == null || url.isEmpty()) {
            throw new IllegalArgumentException("A valid url is required.");
        }
        return new HubConnectionBuilder(url);
    }

    public HubConnectionBuilder withTransport(Transport transport) {
        this.transport = transport;
        return this;
    }


    public HubConnectionBuilder withHttpClient(HttpClient httpClient) {
        this.httpClient = httpClient;
        return this;
    }

    public HubConnectionBuilder configureLogging(LogLevel logLevel) {
        this.logger = new ConsoleLogger(logLevel);
        return this;
    }

    public HubConnectionBuilder withSkipNegotiate(boolean skipNegotiate){
        this.skipNegotiate = skipNegotiate;
        return this;
    }

    public HubConnectionBuilder withAccessTokenProvider(Supplier<CompletableFuture<String>> accessTokenProvider) {
        this.accessTokenProvider = accessTokenProvider;
        return this;
    }

    public HubConnectionBuilder configureLogging(Logger logger) {
        this.logger = logger;
        return this;
    }

    public HubConnectionBuilder withLogger(Logger logger) {
        this.logger = logger;
        return this;
    }

    public HubConnection build() {
        return new HubConnection(url, transport, skipNegotiate, logger, httpClient, accessTokenProvider);
    }
}