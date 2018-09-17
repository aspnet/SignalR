// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

package com.microsoft.aspnet.signalr.test;

import java.util.concurrent.CompletableFuture;

import org.junit.Rule;
import org.junit.Test;
import org.junit.rules.ExpectedException;

import com.microsoft.aspnet.signalr.*;

public class WebSocketTransportTest {

    @Rule
    public ExpectedException expectedEx = ExpectedException.none();

    @Test
    public void WebsocketThrowsIfItCantConnect() throws Exception {
        expectedEx.expect(Exception.class);
        expectedEx.expectMessage("There was an error starting the Websockets transport");
        Transport transport = new WebSocketTransport("www.notarealurl12345.fake", new NullLogger());
        CompletableFuture future = transport.start();
        future.get();

    }
}
