// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

package com.microsoft.aspnet.signalr;

import static org.junit.Assert.*;

import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;
import java.util.concurrent.PriorityBlockingQueue;

import org.junit.Rule;
import org.junit.Test;
import org.junit.rules.ExpectedException;

import com.google.gson.JsonArray;

public class JsonHubProtocolTest {
    private JsonHubProtocol jsonHubProtocol = new JsonHubProtocol();

    @Test
    public void checkProtocolName() {
        assertEquals("json", jsonHubProtocol.getName());
    }

    @Test
    public void checkVersionNumber() {
        assertEquals(1, jsonHubProtocol.getVersion());
    }

    @Test
    public void checkTransferFormat() {
        assertEquals(TransferFormat.Text, jsonHubProtocol.getTransferFormat());
    }

    @Test
    public void VerifyWriteMessage() {
        InvocationMessage invocationMessage = new InvocationMessage("test", new Object[] {"42"});
        String result = jsonHubProtocol.writeMessage(invocationMessage);
        String expectedResult = "{\"type\":1,\"target\":\"test\",\"arguments\":[\"42\"]}\u001E";
        assertEquals(expectedResult, result);
    }

    @Test
    public void ParsePingMessage() throws Exception {
        String stringifiedMessage = "{\"type\":6}\u001E";
        TestBinder binder = new TestBinder(new PingMessage());

        HubMessage[] messages = jsonHubProtocol.parseMessages(stringifiedMessage, binder);

        //We know it's only one message
        assertEquals(1, messages.length);
        assertEquals(HubMessageType.PING, messages[0].getMessageType());
    }

    @Test
    public void ParseCloseMessage() throws Exception {
        String stringifiedMessage = "{\"type\":7}\u001E";
        TestBinder binder = new TestBinder(new CloseMessage());

        HubMessage[] messages = jsonHubProtocol.parseMessages(stringifiedMessage, binder);

        //We know it's only one message
        assertEquals(1, messages.length);

        assertEquals(HubMessageType.CLOSE, messages[0].getMessageType());

        //We can safely cast here because we know that it's a close message.
        CloseMessage closeMessage = (CloseMessage) messages[0];

        assertEquals(null, closeMessage.getError());
    }

    @Test
    public void ParseCloseMessageWithError() throws Exception {
        String stringifiedMessage = "{\"type\":7,\"error\": \"There was an error\"}\u001E";
        TestBinder binder = new TestBinder(new CloseMessage("There was an error"));

        HubMessage[] messages = jsonHubProtocol.parseMessages(stringifiedMessage, binder);

        //We know it's only one message
        assertEquals(1, messages.length);

        assertEquals(HubMessageType.CLOSE, messages[0].getMessageType());

        //We can safely cast here because we know that it's a close message.
        CloseMessage closeMessage = (CloseMessage) messages[0];

        assertEquals("There was an error", closeMessage.getError());
    }

    @Test
    public void ParseSingleMessage() throws Exception {
        String stringifiedMessage = "{\"type\":1,\"target\":\"test\",\"arguments\":[42]}\u001E";
        TestBinder binder = new TestBinder(new InvocationMessage("test", new Object[] { 42 }));

        HubMessage[] messages = jsonHubProtocol.parseMessages(stringifiedMessage, binder);

        //We know it's only one message
        assertEquals(1, messages.length);

        assertEquals(HubMessageType.INVOCATION, messages[0].getMessageType());

        //We can safely cast here because we know that it's an invocation message.
        InvocationMessage invocationMessage = (InvocationMessage) messages[0];

        assertEquals("test", invocationMessage.getTarget());
        assertEquals(null, invocationMessage.getInvocationId());

        int messageResult = (int)invocationMessage.getArguments()[0];
        assertEquals(42, messageResult);
    }

    @Rule
    public ExpectedException exceptionRule = ExpectedException.none();

    @Test
    public void ParseSingleUnsupportedStreamItemMessage() throws Exception {
        exceptionRule.expect(UnsupportedOperationException.class);
        exceptionRule.expectMessage("The message type STREAM_ITEM is not supported yet.");
        String stringifiedMessage = "{\"type\":2,\"Id\":1,\"Item\":42}\u001E";
        TestBinder binder = new TestBinder(null);

        HubMessage[] messages = jsonHubProtocol.parseMessages(stringifiedMessage, binder);
    }

    @Test
    public void ParseSingleUnsupportedStreamInvocationMessage() throws Exception {
        exceptionRule.expect(UnsupportedOperationException.class);
        exceptionRule.expectMessage("The message type STREAM_INVOCATION is not supported yet.");
        String stringifiedMessage = "{\"type\":4,\"Id\":1,\"target\":\"test\",\"arguments\":[42]}\u001E";
        TestBinder binder = new TestBinder(new StreamInvocationMessage("1", "test", new Object[] { 42 }));

        HubMessage[] messages = jsonHubProtocol.parseMessages(stringifiedMessage, binder);
    }

    @Test
    public void ParseSingleUnsupportedCancelInvocationMessage() throws Exception {
        exceptionRule.expect(UnsupportedOperationException.class);
        exceptionRule.expectMessage("The message type CANCEL_INVOCATION is not supported yet.");
        String stringifiedMessage = "{\"type\":5,\"invocationId\":123}\u001E";
        TestBinder binder = new TestBinder(null);

        HubMessage[] messages = jsonHubProtocol.parseMessages(stringifiedMessage, binder);
    }

    @Test
    public void ParseSingleUnsupportedCompletionMessage() throws Exception {
        exceptionRule.expect(UnsupportedOperationException.class);
        exceptionRule.expectMessage("The message type COMPLETION is not supported yet.");
        String stringifiedMessage = "{\"type\":3,\"invocationId\":123}\u001E";
        TestBinder binder = new TestBinder(null);

        HubMessage[] messages = jsonHubProtocol.parseMessages(stringifiedMessage, binder);
    }

    @Test
    public void ParseTwoMessages() throws Exception {
        String twoMessages = "{\"type\":1,\"target\":\"one\",\"arguments\":[42]}\u001E{\"type\":1,\"target\":\"two\",\"arguments\":[43]}\u001E";
        TestBinder binder = new TestBinder(new InvocationMessage("one", new Object[] { 42 }));

        HubMessage[] messages = jsonHubProtocol.parseMessages(twoMessages, binder);
        assertEquals(2, messages.length);

        // Check the first message
        assertEquals(HubMessageType.INVOCATION, messages[0].getMessageType());

        //Now that we know we have an invocation message we can cast the hubMessage.
        InvocationMessage invocationMessage = (InvocationMessage) messages[0];

        assertEquals("one", invocationMessage.getTarget());
        assertEquals(null, invocationMessage.getInvocationId());
        int messageResult = (int)invocationMessage.getArguments()[0];
        assertEquals(42, messageResult);

        // Check the second message
        assertEquals(HubMessageType.INVOCATION, messages[1].getMessageType());

        //Now that we know we have an invocation message we can cast the hubMessage.
        InvocationMessage invocationMessage2 = (InvocationMessage) messages[1];

        assertEquals("two", invocationMessage2.getTarget());
        assertEquals(null, invocationMessage2.getInvocationId());
        int secondMessageResult = (int)invocationMessage2.getArguments()[0];
        assertEquals(43, secondMessageResult);
    }

    @Test
    public void ParseSingleMessageMutipleArgs() throws Exception {
        String stringifiedMessage = "{\"type\":1,\"target\":\"test\",\"arguments\":[42, 24]}\u001E";
        TestBinder binder = new TestBinder(new InvocationMessage("test", new Object[] { 42, 24 }));

        HubMessage[] messages = jsonHubProtocol.parseMessages(stringifiedMessage, binder);

        //We know it's only one message
        assertEquals(HubMessageType.INVOCATION, messages[0].getMessageType());

        InvocationMessage message = (InvocationMessage)messages[0];
        assertEquals("test", message.getTarget());
        assertEquals(null, message.getInvocationId());
        int messageResult = (int) message.getArguments()[0];
        int messageResult2 = (int) message.getArguments()[1];
        assertEquals(42, messageResult);
        assertEquals(24, messageResult2);
    }

    @Test
    public void ParseMessageWithOutOfOrderProperties() throws Exception {
        String stringifiedMessage = "{\"arguments\":[42, 24],\"type\":1,\"target\":\"test\"}\u001E";
        TestBinder binder = new TestBinder(new InvocationMessage("test", new Object[] { 42, 24 }));

        HubMessage[] messages = jsonHubProtocol.parseMessages(stringifiedMessage, binder);

        // We know it's only one message
        assertEquals(HubMessageType.INVOCATION, messages[0].getMessageType());

        InvocationMessage message = (InvocationMessage) messages[0];
        assertEquals("test", message.getTarget());
        assertEquals(null, message.getInvocationId());
        int messageResult = (int) message.getArguments()[0];
        int messageResult2 = (int) message.getArguments()[1];
        assertEquals(42, messageResult);
        assertEquals(24, messageResult2);
    }

    private class TestBinder implements InvocationBinder {
        private Class<?>[] paramTypes = null;

        public TestBinder(HubMessage expectedMessage) {
            if (expectedMessage == null) {
                return;
            }

            switch (expectedMessage.getMessageType()) {
                case STREAM_INVOCATION:
                    ArrayList<Class<?>> streamTypes = new ArrayList<>();
                    for (Object obj : ((StreamInvocationMessage) expectedMessage).getArguments()) {
                        streamTypes.add(obj.getClass());
                    }
                    paramTypes = streamTypes.toArray(new Class<?>[streamTypes.size()]);
                    break;
                case INVOCATION:
                    ArrayList<Class<?>> types = new ArrayList<>();
                    for (Object obj : ((InvocationMessage) expectedMessage).getArguments()) {
                        types.add(obj.getClass());
                    }
                    paramTypes = types.toArray(new Class<?>[types.size()]);
                    break;
                case STREAM_ITEM:
                    break;
                default:
                    break;
            }
        }

        @Override
        public Class<?> GetReturnType(String invocationId) {
            return null;
        }

        @Override
        public List<Class<?>> GetParameterTypes(String methodName) {
            if (paramTypes == null) {
                return new ArrayList<>();
            }
            return new ArrayList<Class<?>>(Arrays.asList(paramTypes));
        }
    }
}