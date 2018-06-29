// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

import com.google.gson.JsonArray;
import org.junit.Test;

import java.util.concurrent.atomic.AtomicInteger;
import java.util.concurrent.atomic.AtomicReference;

import static org.junit.Assert.*;

public class HubConnectionTest {
    @Test
    public void checkHubConnectionState() throws InterruptedException {
        Transport mockTransport = new MockEchoTransport();
        HubConnection hubConnection = new HubConnection("http://example.com", mockTransport);
        hubConnection.start();
        assertTrue(hubConnection.connected);

        hubConnection.stop();
        assertFalse(hubConnection.connected);
    }

    @Test
    public void SendWithNoParamsTriggersOnHandler() throws Exception {
        AtomicReference<Double> value = new AtomicReference<Double>(0.0);
        Transport mockTransport = new MockEchoTransport();
        HubConnection hubConnection = new HubConnection("http://example.com", mockTransport);

        hubConnection.On("inc", () ->{
            assertEquals(0.0, value.get(), 0);
            value.getAndSet(value.get() + 1);
        });

        hubConnection.start();
        hubConnection.send("inc");

        // Confirming that our handler was called and that the counter property was incremented.
        assertEquals(1, value.get(), 0);
    }

    @Test
    public void SendWithParamTriggersOnHandler() throws Exception {
        AtomicReference<String> value = new AtomicReference<>();
        Transport mockTransport = new MockEchoTransport();
        HubConnection hubConnection = new HubConnection("http://example.com", mockTransport);

        hubConnection.On("inc", (param) ->{
            assertNull(value.get());
            value.set(param);
        }, String.class);

        hubConnection.start();
        hubConnection.send("inc", "Hello World");

        // Confirming that our handler was called and the correct message was passed in.
        assertEquals("Hello World", value.get());
    }

    @Test
    public void SendWithTwoParamsTriggersOnHandler() throws Exception {
        AtomicReference<String> value1 = new AtomicReference<>();
        AtomicReference<Double> value2 = new AtomicReference<>();

        Transport mockTransport = new MockEchoTransport();
        HubConnection hubConnection = new HubConnection("http://example.com", mockTransport);

        hubConnection.On("inc", (param1, param2) ->{
            assertNull(value1.get());
            assertNull((value2.get()));

            value1.set(param1);
            value2.set(param2);
        }, String.class, Double.class);

        hubConnection.start();
        hubConnection.send("inc", "Hello World", 12);

        // Confirming that our handler was called and the correct message was passed in.
        assertEquals("Hello World", value1.get());
        assertEquals(12, value2.get(), 0);
    }

    private class MockEchoTransport implements Transport {
        private OnReceiveCallBack onReceiveCallBack;

        @Override
        public void start() {}

        @Override
        public void send(String message) throws Exception {
            this.onReceive(message);
        }

        @Override
        public void setOnReceive(OnReceiveCallBack callback) {
            this.onReceiveCallBack = callback;
        }

        @Override
        public void onReceive(String message) throws Exception {
            this.onReceiveCallBack.invoke(message);
        }

        @Override
        public void stop() {return;}
    }
}