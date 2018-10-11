// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

package com.microsoft.signalr;

import static org.junit.jupiter.api.Assertions.*;

import java.time.Duration;
import java.util.List;
import java.util.concurrent.CancellationException;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.ExecutionException;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.TimeoutException;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.concurrent.atomic.AtomicReference;

import org.junit.jupiter.api.Test;

import io.reactivex.Completable;
import io.reactivex.Single;

class HubConnectionTest {
    private static final String RECORD_SEPARATOR = "\u001e";

    @Test
    public void checkHubConnectionState() {
        HubConnection hubConnection = TestUtils.createHubConnection("http://example.com");
        hubConnection.start().blockingAwait(1000, TimeUnit.MILLISECONDS);
        assertEquals(HubConnectionState.CONNECTED, hubConnection.getConnectionState());

        hubConnection.stop();
        assertEquals(HubConnectionState.DISCONNECTED, hubConnection.getConnectionState());
    }

    @Test
    public void transportCloseTriggersStopInHubConnection() {
        MockTransport mockTransport = new MockTransport();
        HubConnection hubConnection = TestUtils.createHubConnection("http://example.com", mockTransport);
        hubConnection.start().blockingAwait(1000, TimeUnit.MILLISECONDS);
        assertEquals(HubConnectionState.CONNECTED, hubConnection.getConnectionState());
        mockTransport.stop();

        assertEquals(HubConnectionState.DISCONNECTED, hubConnection.getConnectionState());
    }

    @Test
    public void transportCloseWithErrorTriggersStopInHubConnection() {
        MockTransport mockTransport = new MockTransport();
        AtomicReference<String> message = new AtomicReference<>();
        HubConnection hubConnection = TestUtils.createHubConnection("http://example.com", mockTransport);
        String errorMessage = "Example transport error.";

        hubConnection.onClosed((error) -> {
            message.set(error.getMessage());
        });

        hubConnection.start().blockingAwait(1000, TimeUnit.MILLISECONDS);
        assertEquals(HubConnectionState.CONNECTED, hubConnection.getConnectionState());
        mockTransport.stopWithError(errorMessage);
        assertEquals(errorMessage, message.get());
    }

    @Test
    public void checkHubConnectionStateNoHandShakeResponse() {
        MockTransport mockTransport = new MockTransport(false);
        HubConnection hubConnection = HubConnectionBuilder.create("http://example.com")
                .withTransport(mockTransport)
                .withHttpClient(new TestHttpClient())
                .shouldSkipNegotiate(true)
                .withHandshakeResponseTimeout(Duration.ofMillis(100))
                .build();
        Throwable exception = assertThrows(RuntimeException.class, () -> hubConnection.start().blockingAwait(1000, TimeUnit.MILLISECONDS));
        assertEquals(ExecutionException.class, exception.getCause().getClass());
        assertEquals(TimeoutException.class, exception.getCause().getCause().getClass());
        assertEquals(exception.getCause().getCause().getMessage(), "Timed out waiting for the server to respond to the handshake message.");
        assertEquals(HubConnectionState.DISCONNECTED, hubConnection.getConnectionState());
    }

    @Test
    public void constructHubConnectionWithHttpConnectionOptions() {
        Transport mockTransport = new MockTransport();
        HubConnection hubConnection = TestUtils.createHubConnection("http://example.com", mockTransport);

        hubConnection.start();
        assertEquals(HubConnectionState.CONNECTED, hubConnection.getConnectionState());

        hubConnection.stop();
        assertEquals(HubConnectionState.DISCONNECTED, hubConnection.getConnectionState());
    }

    @Test
    public void hubConnectionClosesAfterCloseMessage() {
        MockTransport mockTransport = new MockTransport();
        HubConnection hubConnection = TestUtils.createHubConnection("http://example.com", mockTransport);

        hubConnection.start();

        assertEquals(HubConnectionState.CONNECTED, hubConnection.getConnectionState());

        mockTransport.receiveMessage("{\"type\":7,\"error\": \"There was an error\"}" + RECORD_SEPARATOR);

        assertEquals(HubConnectionState.DISCONNECTED, hubConnection.getConnectionState());
    }

    @Test
    public void invalidHandShakeResponse() {
        MockTransport mockTransport = new MockTransport(false);
        HubConnection hubConnection = TestUtils.createHubConnection("http://example.com", mockTransport);

        hubConnection.start();

        Throwable exception = assertThrows(RuntimeException.class, () -> mockTransport.receiveMessage("{" + RECORD_SEPARATOR));
        assertEquals("An invalid handshake response was received from the server.", exception.getMessage());
        assertEquals(HubConnectionState.DISCONNECTED, hubConnection.getConnectionState());
    }

    @Test
    public void hubConnectionReceiveHandshakeResponseWithError() {
        MockTransport mockTransport = new MockTransport(false);
        HubConnection hubConnection = TestUtils.createHubConnection("http://example.com", mockTransport);

        hubConnection.start();
        Throwable exception = assertThrows(RuntimeException.class, () -> mockTransport.receiveMessage("{\"error\":\"Requested protocol 'messagepack' is not available.\"}" + RECORD_SEPARATOR));
        assertEquals("Error in handshake Requested protocol 'messagepack' is not available.", exception.getMessage());
    }

    @Test
    public void registeringMultipleHandlersAndBothGetTriggered() {
        AtomicReference<Double> value = new AtomicReference<>(0.0);
        MockTransport mockTransport = new MockTransport();
        HubConnection hubConnection = TestUtils.createHubConnection("http://example.com", mockTransport);
        Action action = () -> value.getAndUpdate((val) -> val + 1);

        hubConnection.on("inc", action);
        hubConnection.on("inc", action);

        assertEquals(Double.valueOf(0), value.get());

        hubConnection.start();

        String message = mockTransport.getSentMessages()[0];
        String expectedHanshakeRequest = "{\"protocol\":\"json\",\"version\":1}" + RECORD_SEPARATOR;

        assertEquals(expectedHanshakeRequest, message);

        mockTransport.receiveMessage("{\"type\":1,\"target\":\"inc\",\"arguments\":[]}" + RECORD_SEPARATOR);

        // Confirming that our handler was called and that the counter property was incremented.
        assertEquals(Double.valueOf(2), value.get());
    }

    @Test
    public void removeHandlerByName() {
        AtomicReference<Double> value = new AtomicReference<>(0.0);
        MockTransport mockTransport = new MockTransport();
        HubConnection hubConnection = TestUtils.createHubConnection("http://example.com", mockTransport);
        Action action = () -> value.getAndUpdate((val) -> val + 1);

        hubConnection.on("inc", action);

        assertEquals(Double.valueOf(0), value.get());

        hubConnection.start();
        String message = mockTransport.getSentMessages()[0];
        String expectedHanshakeRequest = "{\"protocol\":\"json\",\"version\":1}" + RECORD_SEPARATOR;

        assertEquals(expectedHanshakeRequest, message);

        mockTransport.receiveMessage("{\"type\":1,\"target\":\"inc\",\"arguments\":[]}" + RECORD_SEPARATOR);

        // Confirming that our handler was called and that the counter property was incremented.
        assertEquals(Double.valueOf(1), value.get());

        hubConnection.remove("inc");
        assertEquals(Double.valueOf(1), value.get());
    }

    @Test
    public void addAndRemoveHandlerImmediately() {
        AtomicReference<Double> value = new AtomicReference<>(0.0);
        MockTransport mockTransport = new MockTransport();
        HubConnection hubConnection = TestUtils.createHubConnection("http://example.com", mockTransport);
        Action action = () -> value.getAndUpdate((val) -> val + 1);

        hubConnection.on("inc", action);
        hubConnection.remove("inc");

        assertEquals(Double.valueOf(0), value.get());

        hubConnection.start();
        String message = mockTransport.getSentMessages()[0];
        String expectedHanshakeRequest = "{\"protocol\":\"json\",\"version\":1}" + RECORD_SEPARATOR;

        assertEquals(expectedHanshakeRequest, message);

        mockTransport.receiveMessage("{\"type\":1,\"target\":\"inc\",\"arguments\":[]}" + RECORD_SEPARATOR);

        // Confirming that the handler was removed.
        assertEquals(Double.valueOf(0), value.get());
    }

    @Test
    public void removingMultipleHandlersWithOneCallToRemove() {
        AtomicReference<Double> value = new AtomicReference<>(0.0);
        MockTransport mockTransport = new MockTransport();
        HubConnection hubConnection = TestUtils.createHubConnection("http://example.com", mockTransport);
        Action action = () -> value.getAndUpdate((val) -> val + 1);
        Action secondAction = () -> value.getAndUpdate((val) -> val + 2);

        hubConnection.on("inc", action);
        hubConnection.on("inc", secondAction);

        assertEquals(Double.valueOf(0), value.get());

        hubConnection.start();
        String message = mockTransport.getSentMessages()[0];
        String expectedHanshakeRequest = "{\"protocol\":\"json\",\"version\":1}" + RECORD_SEPARATOR;

        assertEquals(expectedHanshakeRequest, message);

        mockTransport.receiveMessage("{\"type\":1,\"target\":\"inc\",\"arguments\":[]}" + RECORD_SEPARATOR);

        assertEquals(Double.valueOf(3), value.get());

        hubConnection.remove("inc");

        mockTransport.receiveMessage("{\"type\":1,\"target\":\"inc\",\"arguments\":[]}" + RECORD_SEPARATOR);

        // Confirm that another invocation doesn't change anything because the handlers have been removed.
        assertEquals(Double.valueOf(3), value.get());
    }

    @Test
    public void removeHandlerWithUnsubscribe() {
        AtomicReference<Double> value = new AtomicReference<>(0.0);
        MockTransport mockTransport = new MockTransport();
        HubConnection hubConnection = TestUtils.createHubConnection("http://example.com", mockTransport);
        Action action = () -> value.getAndUpdate((val) -> val + 1);

        Subscription subscription = hubConnection.on("inc", action);

        assertEquals(Double.valueOf(0), value.get());

        hubConnection.start();
        String message = mockTransport.getSentMessages()[0];
        String expectedHanshakeRequest = "{\"protocol\":\"json\",\"version\":1}" + RECORD_SEPARATOR;

        assertEquals(expectedHanshakeRequest, message);

        mockTransport.receiveMessage("{\"type\":1,\"target\":\"inc\",\"arguments\":[]}" + RECORD_SEPARATOR);

        // Confirming that our handler was called and that the counter property was incremented.
        assertEquals(Double.valueOf(1), value.get());

        subscription.unsubscribe();
        try {
            mockTransport.receiveMessage("{\"type\":1,\"target\":\"inc\",\"arguments\":[]}" + RECORD_SEPARATOR);
        } catch (Exception ex) {
            assertEquals("There are no callbacks registered for the method 'inc'.", ex.getMessage());
        }

        assertEquals(Double.valueOf(1), value.get());
    }

    @Test
    public void unsubscribeTwice() {
        AtomicReference<Double> value = new AtomicReference<>(0.0);
        MockTransport mockTransport = new MockTransport();
        HubConnection hubConnection = TestUtils.createHubConnection("http://example.com", mockTransport);
        Action action = () -> value.getAndUpdate((val) -> val + 1);

        Subscription subscription = hubConnection.on("inc", action);

        assertEquals(Double.valueOf(0), value.get());

        hubConnection.start();
        String message = mockTransport.getSentMessages()[0];
        String expectedHanshakeRequest = "{\"protocol\":\"json\",\"version\":1}" + RECORD_SEPARATOR;

        assertEquals(expectedHanshakeRequest, message);

        mockTransport.receiveMessage("{\"type\":1,\"target\":\"inc\",\"arguments\":[]}" + RECORD_SEPARATOR);

        // Confirming that our handler was called and that the counter property was incremented.
        assertEquals(Double.valueOf(1), value.get());

        subscription.unsubscribe();
        subscription.unsubscribe();
        try {
            mockTransport.receiveMessage("{\"type\":1,\"target\":\"inc\",\"arguments\":[]}" + RECORD_SEPARATOR);
        } catch (Exception ex) {
            assertEquals("There are no callbacks registered for the method 'inc'.", ex.getMessage());
        }

        assertEquals(Double.valueOf(1), value.get());
    }

    @Test
    public void removeSingleHandlerWithUnsubscribe() {
        AtomicReference<Double> value = new AtomicReference<>(0.0);
        MockTransport mockTransport = new MockTransport();
        HubConnection hubConnection = TestUtils.createHubConnection("http://example.com", mockTransport);
        Action action = () -> value.getAndUpdate((val) -> val + 1);
        Action secondAction = () -> value.getAndUpdate((val) -> val + 2);

        Subscription subscription = hubConnection.on("inc", action);
        Subscription secondSubscription = hubConnection.on("inc", secondAction);

        assertEquals(Double.valueOf(0), value.get());

        hubConnection.start();
        String message = mockTransport.getSentMessages()[0];
        String expectedHanshakeRequest = "{\"protocol\":\"json\",\"version\":1}" + RECORD_SEPARATOR;

        assertEquals(expectedHanshakeRequest, message);

        mockTransport.receiveMessage("{\"type\":1,\"target\":\"inc\",\"arguments\":[]}" + RECORD_SEPARATOR);
        // Confirming that our handler was called and that the counter property was incremented.
        assertEquals(Double.valueOf(3), value.get());

        // This removes the first handler so when "inc" is invoked secondAction should still run.
        subscription.unsubscribe();
        mockTransport.receiveMessage("{\"type\":1,\"target\":\"inc\",\"arguments\":[]}" + RECORD_SEPARATOR);
        assertEquals(Double.valueOf(5), value.get());
    }

    @Test
    public void addAndRemoveHandlerImmediatelyWithSubscribe() {
        AtomicReference<Double> value = new AtomicReference<>(0.0);
        MockTransport mockTransport = new MockTransport();
        HubConnection hubConnection = TestUtils.createHubConnection("http://example.com", mockTransport);
        Action action = () -> value.getAndUpdate((val) -> val + 1);

        Subscription sub = hubConnection.on("inc", action);
        sub.unsubscribe();

        assertEquals(Double.valueOf(0), value.get());

        hubConnection.start();

        try {
            mockTransport.receiveMessage("{\"type\":1,\"target\":\"inc\",\"arguments\":[]}" + RECORD_SEPARATOR);
        } catch (Exception ex) {
            assertEquals("There are no callbacks registered for the method 'inc'.", ex.getMessage());
        }

        // Confirming that the handler was removed.
        assertEquals(Double.valueOf(0), value.get());
    }

    @Test
    public void registeringMultipleHandlersThatTakeParamsAndBothGetTriggered() {
        AtomicReference<Double> value = new AtomicReference<>(0.0);
        MockTransport mockTransport = new MockTransport();
        HubConnection hubConnection = TestUtils.createHubConnection("http://example.com", mockTransport);

        Action1<Double> action = (number) -> value.getAndUpdate((val) -> val + number);

        hubConnection.on("add", action, Double.class);
        hubConnection.on("add", action, Double.class);

        assertEquals(Double.valueOf(0), value.get());
        hubConnection.start();
        mockTransport.receiveMessage("{\"type\":1,\"target\":\"add\",\"arguments\":[12]}" + RECORD_SEPARATOR);

        // Confirming that our handler was called and the correct message was passed in.
        assertEquals(Double.valueOf(24), value.get());
    }

    @Test
    public void invokeWaitsForCompletionMessage() {
        MockTransport mockTransport = new MockTransport();
        HubConnection hubConnection = TestUtils.createHubConnection("http://example.com", mockTransport);

        hubConnection.start();

        AtomicBoolean done = new AtomicBoolean();
        Single<Integer> result = hubConnection.invoke(Integer.class, "echo", "message");
        result.doOnSuccess(value -> done.set(true));
        assertEquals("{\"type\":1,\"invocationId\":\"1\",\"target\":\"echo\",\"arguments\":[\"message\"]}" + RECORD_SEPARATOR, mockTransport.getSentMessages()[1]);
        assertFalse(done.get());

        mockTransport.receiveMessage("{\"type\":3,\"invocationId\":\"1\",\"result\":42}" + RECORD_SEPARATOR);

        assertEquals(Integer.valueOf(42), result.timeout(1000, TimeUnit.MILLISECONDS).blockingGet());
    }

    @Test
    public void multipleInvokesWaitForOwnCompletionMessage() {
        MockTransport mockTransport = new MockTransport();
        HubConnection hubConnection = TestUtils.createHubConnection("http://example.com", mockTransport);

        hubConnection.start();

        AtomicBoolean doneFirst = new AtomicBoolean();
        AtomicBoolean doneSecond = new AtomicBoolean();
        Single<Integer> result = hubConnection.invoke(Integer.class, "echo", "message");
        Single<String> result2 = hubConnection.invoke(String.class, "echo", "message");
        result.doOnSuccess(value -> doneFirst.set(true));
        result2.doOnSuccess(value -> doneSecond.set(true));
        assertEquals("{\"type\":1,\"invocationId\":\"1\",\"target\":\"echo\",\"arguments\":[\"message\"]}" + RECORD_SEPARATOR, mockTransport.getSentMessages()[1]);
        assertEquals("{\"type\":1,\"invocationId\":\"2\",\"target\":\"echo\",\"arguments\":[\"message\"]}" + RECORD_SEPARATOR, mockTransport.getSentMessages()[2]);
        assertFalse(doneFirst.get());
        assertFalse(doneSecond.get());

        mockTransport.receiveMessage("{\"type\":3,\"invocationId\":\"2\",\"result\":\"message\"}" + RECORD_SEPARATOR);
        assertEquals("message", result2.timeout(1000, TimeUnit.MILLISECONDS).blockingGet());
        assertFalse(doneFirst.get());

        mockTransport.receiveMessage("{\"type\":3,\"invocationId\":\"1\",\"result\":42}" + RECORD_SEPARATOR);
        assertEquals(Integer.valueOf(42), result.timeout(1000, TimeUnit.MILLISECONDS).blockingGet());
    }

    @Test
    public void invokeWorksForPrimitiveTypes() {
        MockTransport mockTransport = new MockTransport();
        HubConnection hubConnection = TestUtils.createHubConnection("http://example.com", mockTransport);

        hubConnection.start();

        AtomicBoolean done = new AtomicBoolean();
        // int.class is a primitive type and since we use Class.cast to cast an Object to the expected return type
        // which does not work for primitives we have to write special logic for that case.
        Single<Integer> result = hubConnection.invoke(int.class, "echo", "message");
        result.doOnSuccess(value -> done.set(true));
        assertFalse(done.get());

        mockTransport.receiveMessage("{\"type\":3,\"invocationId\":\"1\",\"result\":42}" + RECORD_SEPARATOR);

        assertEquals(Integer.valueOf(42), result.timeout(1000, TimeUnit.MILLISECONDS).blockingGet());
    }

    @Test
    public void completionMessageCanHaveError() {
        MockTransport mockTransport = new MockTransport();
        HubConnection hubConnection = TestUtils.createHubConnection("http://example.com", mockTransport);

        hubConnection.start();

        AtomicBoolean done = new AtomicBoolean();
        Single<Integer> result = hubConnection.invoke(int.class, "echo", "message");
        result.doOnSuccess(value -> done.set(true));
        assertFalse(done.get());

        mockTransport.receiveMessage("{\"type\":3,\"invocationId\":\"1\",\"error\":\"There was an error\"}" + RECORD_SEPARATOR);

        String exceptionMessage = null;
        try {
            result.timeout(1000, TimeUnit.MILLISECONDS).blockingGet();
            assertFalse(true);
        } catch (Exception ex) {
            exceptionMessage = ex.getCause().getMessage();
        }

        assertEquals("com.microsoft.signalr.HubException: There was an error", exceptionMessage);
    }

    @Test
    public void stopCancelsActiveInvokes() {
        MockTransport mockTransport = new MockTransport();
        HubConnection hubConnection = TestUtils.createHubConnection("http://example.com", mockTransport);

        hubConnection.start();

        AtomicBoolean done = new AtomicBoolean();
        Single<Integer> result = hubConnection.invoke(int.class, "echo", "message");
        result.doOnSuccess(value -> done.set(true));
        assertFalse(done.get());

        hubConnection.stop();

        boolean hasException = false;
        try {
            result.timeout(1000, TimeUnit.MILLISECONDS).blockingGet();
            assertFalse(true);
        } catch (CancellationException ex) {
            hasException = true;
        }

        assertTrue(hasException);
    }

    @Test
    public void sendWithNoParamsTriggersOnHandler() {
        AtomicReference<Integer> value = new AtomicReference<>(0);
        MockTransport mockTransport = new MockTransport();
        HubConnection hubConnection = TestUtils.createHubConnection("http://example.com", mockTransport);

        hubConnection.on("inc", () ->{
            assertEquals(Integer.valueOf(0), value.get());
            value.getAndUpdate((val) -> val + 1);
        });

        hubConnection.start();
        mockTransport.receiveMessage("{\"type\":1,\"target\":\"inc\",\"arguments\":[]}" + RECORD_SEPARATOR);

        // Confirming that our handler was called and that the counter property was incremented.
        assertEquals(Integer.valueOf(1), value.get());
    }

    @Test
    public void sendWithParamTriggersOnHandler() {
        AtomicReference<String> value = new AtomicReference<>();
        MockTransport mockTransport = new MockTransport();
        HubConnection hubConnection = TestUtils.createHubConnection("http://example.com", mockTransport);

        hubConnection.on("inc", (param) ->{
            assertNull(value.get());
            value.set(param);
        }, String.class);

        hubConnection.start();
        mockTransport.receiveMessage("{\"type\":1,\"target\":\"inc\",\"arguments\":[\"Hello World\"]}" + RECORD_SEPARATOR);
        hubConnection.send("inc", "Hello World");

        // Confirming that our handler was called and the correct message was passed in.
        assertEquals("Hello World", value.get());
    }

    @Test
    public void sendWithTwoParamsTriggersOnHandler() {
        AtomicReference<String> value1 = new AtomicReference<>();
        AtomicReference<Double> value2 = new AtomicReference<>();

        MockTransport mockTransport = new MockTransport();
        HubConnection hubConnection = TestUtils.createHubConnection("http://example.com", mockTransport);

        hubConnection.on("inc", (param1, param2) ->{
            assertNull(value1.get());
            assertNull((value2.get()));

            value1.set(param1);
            value2.set(param2);
        }, String.class, Double.class);

        hubConnection.start();
        mockTransport.receiveMessage("{\"type\":1,\"target\":\"inc\",\"arguments\":[\"Hello World\", 12]}" + RECORD_SEPARATOR);
        hubConnection.send("inc", "Hello World", 12);

        // Confirming that our handler was called and the correct message was passed in.
        assertEquals("Hello World", value1.get());
        assertEquals(Double.valueOf(12), value2.get());
    }

    @Test
    public void sendWithThreeParamsTriggersOnHandler() {
        AtomicReference<String> value1 = new AtomicReference<>();
        AtomicReference<String> value2 = new AtomicReference<>();
        AtomicReference<String> value3 = new AtomicReference<>();

        MockTransport mockTransport = new MockTransport();
        HubConnection hubConnection = TestUtils.createHubConnection("http://example.com", mockTransport);

        hubConnection.on("inc", (param1, param2, param3) ->{
            assertNull(value1.get());
            assertNull(value2.get());
            assertNull(value3.get());

            value1.set(param1);
            value2.set(param2);
            value3.set(param3);
        }, String.class, String.class, String.class);

        hubConnection.start();
        mockTransport.receiveMessage("{\"type\":1,\"target\":\"inc\",\"arguments\":[\"A\", \"B\", \"C\"]}" + RECORD_SEPARATOR);
        hubConnection.send("inc", "A", "B", "C");

        // Confirming that our handler was called and the correct message was passed in.
        assertEquals("A", value1.get());
        assertEquals("B", value2.get());
        assertEquals("C", value3.get());
    }

    @Test
    public void sendWithFourParamsTriggersOnHandler() {
        AtomicReference<String> value1 = new AtomicReference<>();
        AtomicReference<String> value2 = new AtomicReference<>();
        AtomicReference<String> value3 = new AtomicReference<>();
        AtomicReference<String> value4 = new AtomicReference<>();

        MockTransport mockTransport = new MockTransport();
        HubConnection hubConnection = TestUtils.createHubConnection("http://example.com", mockTransport);

        hubConnection.on("inc", (param1, param2, param3, param4) ->{
            assertNull(value1.get());
            assertNull(value2.get());
            assertNull(value3.get());
            assertNull(value4.get());

            value1.set(param1);
            value2.set(param2);
            value3.set(param3);
            value4.set(param4);
        }, String.class, String.class, String.class, String.class);

        hubConnection.start();
        mockTransport.receiveMessage("{\"type\":1,\"target\":\"inc\",\"arguments\":[\"A\", \"B\", \"C\", \"D\"]}" + RECORD_SEPARATOR);

        // Confirming that our handler was called and the correct message was passed in.
        assertEquals("A", value1.get());
        assertEquals("B", value2.get());
        assertEquals("C", value3.get());
        assertEquals("D", value4.get());
    }

    @Test
    public void sendWithFiveParamsTriggersOnHandler()  {
        AtomicReference<String> value1 = new AtomicReference<>();
        AtomicReference<String> value2 = new AtomicReference<>();
        AtomicReference<String> value3 = new AtomicReference<>();
        AtomicReference<Boolean> value4 = new AtomicReference<>();
        AtomicReference<Double> value5 = new AtomicReference<>();

        MockTransport mockTransport = new MockTransport();
        HubConnection hubConnection = TestUtils.createHubConnection("http://example.com", mockTransport);

        hubConnection.on("inc", (param1, param2, param3, param4, param5) ->{
            assertNull(value1.get());
            assertNull(value2.get());
            assertNull(value3.get());
            assertNull(value4.get());
            assertNull(value5.get());

            value1.set(param1);
            value2.set(param2);
            value3.set(param3);
            value4.set(param4);
            value5.set(param5);
        }, String.class, String.class, String.class, Boolean.class, Double.class);

        hubConnection.start();
        mockTransport.receiveMessage("{\"type\":1,\"target\":\"inc\",\"arguments\":[\"A\", \"B\", \"C\",true,12 ]}" + RECORD_SEPARATOR);

        // Confirming that our handler was called and the correct message was passed in.
        assertEquals("A", value1.get());
        assertEquals("B", value2.get());
        assertEquals("C", value3.get());
        assertTrue(value4.get());
        assertEquals(Double.valueOf(12), value5.get());
    }

    @Test
    public void sendWithSixParamsTriggersOnHandler()  {
        AtomicReference<String> value1 = new AtomicReference<>();
        AtomicReference<String> value2 = new AtomicReference<>();
        AtomicReference<String> value3 = new AtomicReference<>();
        AtomicReference<Boolean> value4 = new AtomicReference<>();
        AtomicReference<Double> value5 = new AtomicReference<>();
        AtomicReference<String> value6 = new AtomicReference<>();

        MockTransport mockTransport = new MockTransport();
        HubConnection hubConnection = TestUtils.createHubConnection("http://example.com", mockTransport);

        hubConnection.on("inc", (param1, param2, param3, param4, param5, param6) -> {
            assertNull(value1.get());
            assertNull(value2.get());
            assertNull(value3.get());
            assertNull(value4.get());
            assertNull(value5.get());
            assertNull(value6.get());

            value1.set(param1);
            value2.set(param2);
            value3.set(param3);
            value4.set(param4);
            value5.set(param5);
            value6.set(param6);
        }, String.class, String.class, String.class, Boolean.class, Double.class, String.class);

        hubConnection.start();
        mockTransport.receiveMessage("{\"type\":1,\"target\":\"inc\",\"arguments\":[\"A\", \"B\", \"C\",true,12,\"D\"]}" + RECORD_SEPARATOR);

        // Confirming that our handler was called and the correct message was passed in.
        assertEquals("A", value1.get());
        assertEquals("B", value2.get());
        assertEquals("C", value3.get());
        assertTrue(value4.get());
        assertEquals(Double.valueOf(12), value5.get());
        assertEquals("D", value6.get());
    }

    @Test
    public void sendWithSevenParamsTriggersOnHandler()  {
        AtomicReference<String> value1 = new AtomicReference<>();
        AtomicReference<String> value2 = new AtomicReference<>();
        AtomicReference<String> value3 = new AtomicReference<>();
        AtomicReference<Boolean> value4 = new AtomicReference<>();
        AtomicReference<Double> value5 = new AtomicReference<>();
        AtomicReference<String> value6 = new AtomicReference<>();
        AtomicReference<String> value7 = new AtomicReference<>();

        MockTransport mockTransport = new MockTransport();
        HubConnection hubConnection = TestUtils.createHubConnection("http://example.com", mockTransport);

        hubConnection.on("inc", (param1, param2, param3, param4, param5, param6, param7) -> {
            assertNull(value1.get());
            assertNull(value2.get());
            assertNull(value3.get());
            assertNull(value4.get());
            assertNull(value5.get());
            assertNull(value6.get());
            assertNull(value7.get());

            value1.set(param1);
            value2.set(param2);
            value3.set(param3);
            value4.set(param4);
            value5.set(param5);
            value6.set(param6);
            value7.set(param7);
        }, String.class, String.class, String.class, Boolean.class, Double.class, String.class, String.class);

        hubConnection.start();
        mockTransport.receiveMessage("{\"type\":1,\"target\":\"inc\",\"arguments\":[\"A\", \"B\", \"C\",true,12,\"D\",\"E\"]}" + RECORD_SEPARATOR);

        // Confirming that our handler was called and the correct message was passed in.
        assertEquals("A", value1.get());
        assertEquals("B", value2.get());
        assertEquals("C", value3.get());
        assertTrue(value4.get());
        assertEquals(Double.valueOf(12), value5.get());
        assertEquals("D", value6.get());
        assertEquals("E", value7.get());
    }

    @Test
    public void sendWithEightParamsTriggersOnHandler()  {
        AtomicReference<String> value1 = new AtomicReference<>();
        AtomicReference<String> value2 = new AtomicReference<>();
        AtomicReference<String> value3 = new AtomicReference<>();
        AtomicReference<Boolean> value4 = new AtomicReference<>();
        AtomicReference<Double> value5 = new AtomicReference<>();
        AtomicReference<String> value6 = new AtomicReference<>();
        AtomicReference<String> value7 = new AtomicReference<>();
        AtomicReference<String> value8 = new AtomicReference<>();

        MockTransport mockTransport = new MockTransport();
        HubConnection hubConnection = TestUtils.createHubConnection("http://example.com", mockTransport);

        hubConnection.on("inc", (param1, param2, param3, param4, param5, param6, param7, param8) -> {
            assertNull(value1.get());
            assertNull(value2.get());
            assertNull(value3.get());
            assertNull(value4.get());
            assertNull(value5.get());
            assertNull(value6.get());
            assertNull(value7.get());
            assertNull(value8.get());

            value1.set(param1);
            value2.set(param2);
            value3.set(param3);
            value4.set(param4);
            value5.set(param5);
            value6.set(param6);
            value7.set(param7);
            value8.set(param8);
        }, String.class, String.class, String.class, Boolean.class, Double.class, String.class, String.class, String.class);

        hubConnection.start();
        mockTransport.receiveMessage("{\"type\":1,\"target\":\"inc\",\"arguments\":[\"A\", \"B\", \"C\",true,12,\"D\",\"E\",\"F\"]}" + RECORD_SEPARATOR);
        // Confirming that our handler was called and the correct message was passed in.
        assertEquals("A", value1.get());
        assertEquals("B", value2.get());
        assertEquals("C", value3.get());
        assertTrue(value4.get());
        assertEquals(Double.valueOf(12), value5.get());
        assertEquals("D", value6.get());
        assertEquals("E", value7.get());
        assertEquals("F", value8.get());
    }

    private class Custom {
        public int number;
        public String str;
        public boolean[] bools;
    }

    @Test
    public void sendWithCustomObjectTriggersOnHandler()  {
        AtomicReference<Custom> value1 = new AtomicReference<>();

        MockTransport mockTransport = new MockTransport();
        HubConnection hubConnection = TestUtils.createHubConnection("http://example.com", mockTransport);

        hubConnection.on("inc", (param1) -> {
            assertNull(value1.get());

            value1.set(param1);
        }, Custom.class);

        hubConnection.start();
        mockTransport.receiveMessage("{\"type\":1,\"target\":\"inc\",\"arguments\":[{\"number\":1,\"str\":\"A\",\"bools\":[true,false]}]}" + RECORD_SEPARATOR);

        // Confirming that our handler was called and the correct message was passed in.
        Custom custom = value1.get();
        assertEquals(1, custom.number);
        assertEquals("A", custom.str);
        assertEquals(2, custom.bools.length);
        assertEquals(true, custom.bools[0]);
        assertEquals(false, custom.bools[1]);
    }

    @Test
    public void receiveHandshakeResponseAndMessage()  {
        AtomicReference<Double> value = new AtomicReference<Double>(0.0);
        MockTransport mockTransport = new MockTransport(false);
        HubConnection hubConnection = TestUtils.createHubConnection("http://example.com", mockTransport);

        hubConnection.on("inc", () ->{
            assertEquals(Double.valueOf(0), value.get());
            value.getAndUpdate((val) -> val + 1);
        });

        // On start we're going to receive the handshake response and also an invocation in the same payload.
        hubConnection.start();
        String expectedSentMessage  = "{\"protocol\":\"json\",\"version\":1}" + RECORD_SEPARATOR;
        assertEquals(expectedSentMessage, mockTransport.getSentMessages()[0]);

        mockTransport.receiveMessage("{}" + RECORD_SEPARATOR + "{\"type\":1,\"target\":\"inc\",\"arguments\":[]}" + RECORD_SEPARATOR);

        // Confirming that our handler was called and that the counter property was incremented.
        assertEquals(Double.valueOf(1), value.get());
    }

    @Test
    public void onClosedCallbackRunsWhenStopIsCalled()  {
        AtomicReference<String> value1 = new AtomicReference<>();
        HubConnection hubConnection = TestUtils.createHubConnection("http://example.com");
        hubConnection.start();
        hubConnection.onClosed((ex) -> {
            assertNull(value1.get());
            value1.set("Closed callback ran.");
        });
        hubConnection.stop();

        assertEquals(HubConnectionState.DISCONNECTED, hubConnection.getConnectionState());
        assertEquals(value1.get(), "Closed callback ran.");
    }

    @Test
    public void multipleOnClosedCallbacksRunWhenStopIsCalled()  {
        AtomicReference<String> value1 = new AtomicReference<>();
        AtomicReference<String> value2 = new AtomicReference<>();
        HubConnection hubConnection = TestUtils.createHubConnection("http://example.com");
        hubConnection.start();

        hubConnection.onClosed((ex) -> {
            assertNull(value1.get());
            value1.set("Closed callback ran.");
        });

        hubConnection.onClosed((ex) -> {
            assertNull(value2.get());
            value2.set("The second onClosed callback ran");
        });

        assertNull(value1.get());
        assertNull(value2.get());
        hubConnection.stop();

        assertEquals(HubConnectionState.DISCONNECTED, hubConnection.getConnectionState());
        assertEquals("Closed callback ran.",value1.get());
        assertEquals("The second onClosed callback ran", value2.get());
    }

    @Test
    public void hubConnectionClosesAndRunsOnClosedCallbackAfterCloseMessageWithError()  {
        MockTransport mockTransport = new MockTransport();
        HubConnection hubConnection = TestUtils.createHubConnection("http://example.com", mockTransport);
        hubConnection.onClosed((ex) -> {
            assertEquals(ex.getMessage(), "There was an error");
        });
        hubConnection.start();

        assertEquals(HubConnectionState.CONNECTED, hubConnection.getConnectionState());

        mockTransport.receiveMessage("{\"type\":7,\"error\": \"There was an error\"}" + RECORD_SEPARATOR);

        assertEquals(HubConnectionState.DISCONNECTED, hubConnection.getConnectionState());
    }

    @Test
    public void callingStartOnStartedHubConnectionNoOps()  {
        HubConnection hubConnection = TestUtils.createHubConnection("http://example.com");
        hubConnection.start();
        assertEquals(HubConnectionState.CONNECTED, hubConnection.getConnectionState());

        hubConnection.start();
        assertEquals(HubConnectionState.CONNECTED, hubConnection.getConnectionState());

        hubConnection.stop();
        assertEquals(HubConnectionState.DISCONNECTED, hubConnection.getConnectionState());
    }

    @Test
    public void cannotSendBeforeStart()  {
        HubConnection hubConnection = TestUtils.createHubConnection("http://example.com");
        assertEquals(HubConnectionState.DISCONNECTED, hubConnection.getConnectionState());

        Throwable exception = assertThrows(RuntimeException.class, () -> hubConnection.send("inc"));
        assertEquals("The 'send' method cannot be called if the connection is not active", exception.getMessage());
    }

    @Test
    public void errorWhenReceivingInvokeWithIncorrectArgumentLength()  {
        MockTransport mockTransport = new MockTransport();
        HubConnection hubConnection = TestUtils.createHubConnection("http://example.com", mockTransport);
        hubConnection.on("Send", (s) -> {
            assertTrue(false);
        }, String.class);

        Completable startFuture = hubConnection.start();

        startFuture.blockingAwait(1000, TimeUnit.MILLISECONDS);
        RuntimeException exception = assertThrows(RuntimeException.class, () -> mockTransport.receiveMessage("{\"type\":1,\"target\":\"Send\",\"arguments\":[]}" + RECORD_SEPARATOR));
        assertEquals("Invocation provides 0 argument(s) but target expects 1.", exception.getMessage());
    }

    @Test
    public void negotiateSentOnStart() {
        TestHttpClient client = new TestHttpClient()
        .on("POST", (req) -> CompletableFuture.completedFuture(new HttpResponse(404, "", "")));

        HubConnection hubConnection = HubConnectionBuilder
                .create("http://example.com")
                .withHttpClient(client)
                .build();

        try {
            hubConnection.start().blockingAwait(1000, TimeUnit.MILLISECONDS);
        } catch(Exception ex) {}

        List<HttpRequest> sentRequests = client.getSentRequests();
        assertEquals(1, sentRequests.size());
        assertEquals("http://example.com/negotiate", sentRequests.get(0).getUrl());
    }

    @Test
    public void negotiateThatRedirectsForeverFailsAfter100Tries() {
        TestHttpClient client = new TestHttpClient().on("POST", "http://example.com/negotiate",
                (req) -> CompletableFuture.completedFuture(new HttpResponse(200, "", "{\"url\":\"http://example.com\"}")));

        HubConnection hubConnection = HubConnectionBuilder
            .create("http://example.com")
            .withHttpClient(client)
            .build();

        RuntimeException exception = assertThrows(RuntimeException.class,
            () -> hubConnection.start().blockingAwait(1000, TimeUnit.MILLISECONDS));
        assertEquals("Negotiate redirection limit exceeded.", exception.getCause().getCause().getMessage());
    }

    @Test
    public void afterSuccessfulNegotiateConnectsWithTransport() {
        TestHttpClient client = new TestHttpClient().on("POST", "http://example.com/negotiate",
                (req) -> CompletableFuture.completedFuture(new HttpResponse(200, "",
                        "{\"connectionId\":\"bVOiRPG8-6YiJ6d7ZcTOVQ\",\""
                                + "availableTransports\":[{\"transport\":\"WebSockets\",\"transferFormats\":[\"Text\",\"Binary\"]}]}")));

        MockTransport transport = new MockTransport(true);
        HubConnection hubConnection = HubConnectionBuilder
                .create("http://example.com")
                .withTransport(transport)
                .withHttpClient(client)
                .build();

        hubConnection.start().blockingAwait(1000, TimeUnit.MILLISECONDS);

        String[] sentMessages = transport.getSentMessages();
        assertEquals(1, sentMessages.length);
        assertEquals("{\"protocol\":\"json\",\"version\":1}" + RECORD_SEPARATOR, sentMessages[0]);
    }

    @Test
    public void negotiateThatReturnsErrorThrowsFromStart() {
        TestHttpClient client = new TestHttpClient().on("POST", "http://example.com/negotiate",
                (req) -> CompletableFuture.completedFuture(new HttpResponse(200, "", "{\"error\":\"Test error.\"}")));

        MockTransport transport = new MockTransport(true);
        HubConnection hubConnection = HubConnectionBuilder
                .create("http://example.com")
                .withHttpClient(client)
                .withTransport(transport)
                .build();

        RuntimeException exception = assertThrows(RuntimeException.class,
            () -> hubConnection.start().blockingAwait(1000, TimeUnit.MILLISECONDS));
        assertEquals("Test error.", exception.getCause().getCause().getMessage());
    }

    @Test
    public void negotiateRedirectIsFollowed()  {
        TestHttpClient client = new TestHttpClient().on("POST", "http://example.com/negotiate",
                (req) -> CompletableFuture.completedFuture(new HttpResponse(200, "", "{\"url\":\"http://testexample.com/\"}")))
                .on("POST", "http://testexample.com/negotiate",
                (req) -> CompletableFuture.completedFuture(new HttpResponse(200, "", "{\"connectionId\":\"bVOiRPG8-6YiJ6d7ZcTOVQ\",\""
                + "availableTransports\":[{\"transport\":\"WebSockets\",\"transferFormats\":[\"Text\",\"Binary\"]}]}")));

        MockTransport transport = new MockTransport(true);
        HubConnection hubConnection = HubConnectionBuilder
                .create("http://example.com")
                .withTransport(transport)
                .withHttpClient(client)
                .build();

        hubConnection.start().blockingAwait(1000, TimeUnit.MILLISECONDS);
        assertEquals(HubConnectionState.CONNECTED, hubConnection.getConnectionState());
        hubConnection.stop();
    }

    @Test
    public void accessTokenProviderIsUsedForNegotiate() {
        AtomicReference<String> token = new AtomicReference<>();
        TestHttpClient client = new TestHttpClient()
                .on("POST", "http://example.com/negotiate",
                        (req) -> {
                            token.set(req.getHeaders().get("Authorization"));
                            return CompletableFuture
                                .completedFuture(new HttpResponse(200, "", "{\"connectionId\":\"bVOiRPG8-6YiJ6d7ZcTOVQ\",\""
                                + "availableTransports\":[{\"transport\":\"WebSockets\",\"transferFormats\":[\"Text\",\"Binary\"]}]}"));
                        });

        MockTransport transport = new MockTransport(true);
        HubConnection hubConnection = HubConnectionBuilder
                .create("http://example.com")
                .withTransport(transport)
                .withHttpClient(client)
                .withAccessTokenProvider(Single.just("secretToken"))
                .build();

        hubConnection.start().blockingAwait(1000, TimeUnit.MILLISECONDS);
        assertEquals(HubConnectionState.CONNECTED, hubConnection.getConnectionState());
        hubConnection.stop();
        assertEquals("Bearer secretToken", token.get());
    }

    @Test
    public void accessTokenProviderIsOverriddenFromRedirectNegotiate() {
        AtomicReference<String> token = new AtomicReference<>();
        TestHttpClient client = new TestHttpClient()
            .on("POST", "http://example.com/negotiate", (req) -> CompletableFuture.completedFuture(new HttpResponse(200, "", "{\"url\":\"http://testexample.com/\",\"accessToken\":\"newToken\"}")))
            .on("POST", "http://testexample.com/negotiate", (req) -> {
                token.set(req.getHeaders().get("Authorization"));
                return CompletableFuture
                .completedFuture(new HttpResponse(200, "", "{\"connectionId\":\"bVOiRPG8-6YiJ6d7ZcTOVQ\",\""
                + "availableTransports\":[{\"transport\":\"WebSockets\",\"transferFormats\":[\"Text\",\"Binary\"]}]}"));
            });

        MockTransport transport = new MockTransport(true);
        HubConnection hubConnection = HubConnectionBuilder
                .create("http://example.com")
                .withTransport(transport)
                .withHttpClient(client)
                .withAccessTokenProvider(Single.just("secretToken"))
                .build();

        hubConnection.start().blockingAwait(1000, TimeUnit.MILLISECONDS);
        assertEquals(HubConnectionState.CONNECTED, hubConnection.getConnectionState());
        assertEquals("http://testexample.com/?id=bVOiRPG8-6YiJ6d7ZcTOVQ", transport.getUrl());
        hubConnection.stop();
        assertEquals("Bearer newToken", token.get());
    }

    @Test
    public void connectionTimesOutIfServerDoesNotSendMessage() throws InterruptedException, ExecutionException, TimeoutException {
        HubConnection hubConnection = TestUtils.createHubConnection("http://example.com");
        hubConnection.setServerTimeout(Duration.ofMillis(1));
        hubConnection.setTickRate(Duration.ofMillis(1));
        CompletableFuture<Exception> closedFuture = new CompletableFuture<>();
        hubConnection.onClosed((e) -> {
            closedFuture.complete(e);
        });

        hubConnection.start().blockingAwait(1000, TimeUnit.MILLISECONDS);

        assertEquals("Server timeout elapsed without receiving a message from the server.", closedFuture.get(1000, TimeUnit.MILLISECONDS).getMessage());
    }

    @Test
    public void connectionSendsPingsRegularly() throws InterruptedException {
        MockTransport mockTransport = new MockTransport(true, false);
        HubConnection hubConnection = TestUtils.createHubConnection("http://example.com", mockTransport);
        hubConnection.setKeepAliveInterval(Duration.ofMillis(1));
        hubConnection.setTickRate(Duration.ofMillis(1));

        hubConnection.start().blockingAwait(1000, TimeUnit.MILLISECONDS);

        TimeUnit.MILLISECONDS.sleep(100);
        hubConnection.stop();

        String[] sentMessages = mockTransport.getSentMessages();
        assertTrue(sentMessages.length > 1);
        for (int i = 1; i < sentMessages.length; i++) {
            assertEquals("{\"type\":6}" + RECORD_SEPARATOR, sentMessages[i]);
        }
    }

    @Test
    public void headersAreSetAndSentThroughBuilder() {
        AtomicReference<String> header = new AtomicReference<>();
        TestHttpClient client = new TestHttpClient()
                .on("POST", "http://example.com/negotiate",
                        (req) -> {
                            header.set(req.getHeaders().get("ExampleHeader"));
                            return CompletableFuture
                                    .completedFuture(new HttpResponse(200, "", "{\"connectionId\":\"bVOiRPG8-6YiJ6d7ZcTOVQ\",\""
                                            + "availableTransports\":[{\"transport\":\"WebSockets\",\"transferFormats\":[\"Text\",\"Binary\"]}]}"));
                        });


        MockTransport transport = new MockTransport();
        HubConnection hubConnection = HubConnectionBuilder.create("http://example.com")
                .withTransport(transport)
                .withHttpClient(client)
                .withHeader("ExampleHeader", "ExampleValue")
                .build();

        hubConnection.start().blockingAwait(1000, TimeUnit.MILLISECONDS);
        assertEquals(HubConnectionState.CONNECTED, hubConnection.getConnectionState());
        hubConnection.stop();
        assertEquals("ExampleValue", header.get());
    }

    @Test
    public void sameHeaderSetTwiceGetsOverwritten() {
        AtomicReference<String> header = new AtomicReference<>();
        TestHttpClient client = new TestHttpClient()
                .on("POST", "http://example.com/negotiate",
                        (req) -> {
                            header.set(req.getHeaders().get("ExampleHeader"));
                            return CompletableFuture
                                    .completedFuture(new HttpResponse(200, "", "{\"connectionId\":\"bVOiRPG8-6YiJ6d7ZcTOVQ\",\""
                                            + "availableTransports\":[{\"transport\":\"WebSockets\",\"transferFormats\":[\"Text\",\"Binary\"]}]}"));
                        });


        MockTransport transport = new MockTransport();
        HubConnection hubConnection = HubConnectionBuilder.create("http://example.com")
                .withTransport(transport)
                .withHttpClient(client)
                .withHeader("ExampleHeader", "ExampleValue")
                .withHeader("ExampleHeader", "New Value")
                .build();

        hubConnection.start().blockingAwait(1000, TimeUnit.MILLISECONDS);
        assertEquals(HubConnectionState.CONNECTED, hubConnection.getConnectionState());
        hubConnection.stop();
        assertEquals("New Value", header.get());
    }

    @Test
    public void hubConnectionCanBeStartedAfterBeingStopped() throws Exception {
        MockTransport transport = new MockTransport();
        HubConnection hubConnection = HubConnectionBuilder
                .create("http://example.com")
                .withTransport(transport)
                .shouldSkipNegotiate(true)
                .build();

        hubConnection.start().blockingAwait(1000, TimeUnit.MILLISECONDS);
        assertEquals(HubConnectionState.CONNECTED, hubConnection.getConnectionState());
        hubConnection.stop().blockingAwait(1000, TimeUnit.MILLISECONDS);
        assertEquals(HubConnectionState.DISCONNECTED, hubConnection.getConnectionState());
        hubConnection.start().blockingAwait(1000, TimeUnit.MILLISECONDS);
        assertEquals(HubConnectionState.CONNECTED, hubConnection.getConnectionState());
        hubConnection.stop().blockingAwait(1000, TimeUnit.MILLISECONDS);
        assertEquals(HubConnectionState.DISCONNECTED, hubConnection.getConnectionState());
    }

    @Test
    public void hubConnectionCanBeStartedAfterBeingStoppedAndRedirected()  {
        MockTransport mockTransport = new MockTransport();
        TestHttpClient client = new TestHttpClient()
                .on("POST", "http://example.com/negotiate", (req) -> CompletableFuture
                    .completedFuture(new HttpResponse(200, "", "{\"url\":\"http://testexample.com/\"}")))
                .on("POST", "http://testexample.com/negotiate", (req) -> CompletableFuture
                    .completedFuture(new HttpResponse(200, "", "{\"connectionId\":\"bVOiRPG8-6YiJ6d7ZcTOVQ\",\""
                        + "availableTransports\":[{\"transport\":\"WebSockets\",\"transferFormats\":[\"Text\",\"Binary\"]}]}")));

        HubConnection hubConnection = HubConnectionBuilder
                .create("http://example.com")
                .withTransport(mockTransport)
                .withHttpClient(client)
                .build();

        hubConnection.start().blockingAwait(1000, TimeUnit.MILLISECONDS);
        assertEquals(HubConnectionState.CONNECTED, hubConnection.getConnectionState());
        hubConnection.stop().blockingAwait(1000, TimeUnit.MILLISECONDS);
        assertEquals(HubConnectionState.DISCONNECTED, hubConnection.getConnectionState());
        hubConnection.start().blockingAwait(1000, TimeUnit.MILLISECONDS);
        assertEquals(HubConnectionState.CONNECTED, hubConnection.getConnectionState());
        hubConnection.stop().blockingAwait(1000, TimeUnit.MILLISECONDS);
        assertEquals(HubConnectionState.DISCONNECTED, hubConnection.getConnectionState());
    }

    @Test
    public void non200FromNegotiateThrowsError() {
        TestHttpClient client = new TestHttpClient()
                .on("POST", "http://example.com/negotiate",
                        (req) -> CompletableFuture
                                .completedFuture(new HttpResponse(500, "Internal server error", "")));

        MockTransport transport = new MockTransport();
        HubConnection hubConnection = HubConnectionBuilder
                .create("http://example.com")
                .withTransport(transport)
                .withHttpClient(client)
                .build();

        RuntimeException exception = assertThrows(RuntimeException.class,
            () -> hubConnection.start().blockingAwait(1000, TimeUnit.MILLISECONDS));
        assertEquals("Unexpected status code returned from negotiate: 500 Internal server error.", exception.getCause().getCause().getMessage());
    }
}