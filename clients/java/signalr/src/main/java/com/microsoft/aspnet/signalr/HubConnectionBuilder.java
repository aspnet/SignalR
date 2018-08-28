// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

package com.microsoft.aspnet.signalr;

public class HubConnectionBuilder {
    private boolean built;
    private String url;
    private Transport transport;
    private Logger logger;
    private boolean skipNeotiate;

    public HubConnectionBuilder withUrl(String url) {
        this.url = url;
        return this;
    }

    public HubConnectionBuilder withUrl(String url, Transport transport) {
        this.url = url;
        this.transport = transport;
        return this;
    }

    public HubConnectionBuilder configureLogging(LogLevel logLevel) {
        this.logger = new ConsoleLogger(logLevel);
        return this;
    }

    public HubConnectionBuilder configureLogging(Logger logger) {
        this.logger = logger;
        return this;
    }

    public HubConnectionBuilder skipNeotiate(boolean skip) {
        this.skipNeotiate = skip;
        return this;
    }

    public HubConnection build() throws Exception {
        if (!built) {
            built = true;
            return new HubConnection(url, transport, logger, skipNeotiate);
        }
        throw new Exception("HubConnectionBuilder allows creation only of a single instance of HubConnection.");
    }
}