// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

package com.microsoft.aspnet.signalr;

import static org.junit.jupiter.api.Assertions.*;

import java.util.HashMap;
import java.util.concurrent.TimeUnit;

import org.junit.jupiter.api.Test;

class WebSocketTransportTest {
    @Test
    public void WebsocketThrowsIfItCantConnect() throws Exception {
        Transport transport = new WebSocketTransport("http://www.notarealurl12345.fake", new HashMap<>(), new TestHttpClient(), new NullLogger());
        RuntimeException exception = assertThrows(RuntimeException.class, () -> transport.start().get(1,TimeUnit.SECONDS));
        assertEquals("WebSockets isn't supported in testing currently.", exception.getMessage());
    }
}
