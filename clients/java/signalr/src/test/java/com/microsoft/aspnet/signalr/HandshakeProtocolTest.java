// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

package com.microsoft.aspnet.signalr;

import static org.junit.jupiter.api.Assertions.*;

import org.junit.jupiter.api.Test;

public class HandshakeProtocolTest {

    @Test
    public void VerifyCreateHandshakerequestMessage() {
        HandshakeRequestMessage handshakeRequest = new HandshakeRequestMessage("json", 1);
        String result = HandshakeProtocol.createHandshakeRequestMessage(handshakeRequest);
        String expectedResult = "{\"protocol\":\"json\",\"version\":1}\u001E";
        assertEquals(expectedResult, result);
    }

    @Test
    public void VerifyParseEmptyHandshakeResponseMessage() {
        String emptyHandshakeResponse = "{}";
        HandshakeResponseMessage hsr = HandshakeProtocol.parseHandshakeResponse(emptyHandshakeResponse);
        assertNull(hsr.error);
    }

    @Test
    public void VerifyParseHandshakeResponseMessage() {
        String handshakeResponseWithError = "{\"error\": \"Requested protocol \'messagepack\' is not available.\"}";
        HandshakeResponseMessage hsr = HandshakeProtocol.parseHandshakeResponse(handshakeResponseWithError);
        assertEquals(hsr.error, "Requested protocol 'messagepack' is not available.");
    }
}