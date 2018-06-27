// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

import com.google.gson.JsonArray;
import org.junit.Test;

import static org.junit.Assert.*;

public class HubConnectionTest {
    @Test
    public void checkHubConnectionState() throws InterruptedException {
        Transport mockTransport = new MockEchoTransport();
        HubConnection hubConnection = new HubConnection("test", mockTransport);
            hubConnection.start();
            assertTrue(hubConnection.connected);

            hubConnection.stop();
            assertFalse(hubConnection.connected);
    }

    @Test
    public void SendWithNoParamsTriggersOnHandler() throws InterruptedException {
        TestObject obj = new TestObject();
        Transport mockTransport = new MockEchoTransport();
        HubConnection hubConnection = new HubConnection("placeholder", mockTransport);

        Action callback = (param) -> {
            assertEquals(0, obj.counter);
            obj.incrementCounter();
        };
        hubConnection.On("inc", callback);

        hubConnection.start();
        hubConnection.send("inc");

        // Confirming that our handlers was called and that the counter property was incremented.
        assertEquals(1, obj.counter);
    }

    @Test
    public void SendWithParamTriggersOnHandler() throws InterruptedException {
        TestObject obj = new TestObject();
        Transport mockTransport = new MockEchoTransport();
        HubConnection hubConnection = new HubConnection("http://example.com", mockTransport);

        Action callback = (param) -> {
            assertNull(obj.message);
            String message = ((JsonArray) param).get(0).getAsString();
            obj.message = message;
        };
        hubConnection.On("inc", callback);

        hubConnection.start();
        hubConnection.send("inc", "Hello World");

        // Confirming that our handler was called and the correct message was passed in.
        assertEquals("Hello World", obj.message);
    }

    private class MockEchoTransport implements Transport {
        private OnReceiveCallBack onReceiveCallBack;

        @Override
        public void start() throws InterruptedException {}

        @Override
        public void send(String message) {
            this.onReceive(message);
        }

        @Override
        public void setOnReceive(OnReceiveCallBack callback) {
            this.onReceiveCallBack = callback;
        }

        @Override
        public void onReceive(String message) {
            this.onReceiveCallBack.invoke(message);
        }

        @Override
        public void stop() {return;}
    }

    private class TestObject {
        public int counter = 0;
        public String message;
        public void incrementCounter() {
            counter++;
        }
    }
}