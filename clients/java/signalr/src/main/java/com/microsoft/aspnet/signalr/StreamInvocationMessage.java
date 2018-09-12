// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

package com.microsoft.aspnet.signalr;

class StreamInvocationMessage extends InvocationMessage {

    int type = HubMessageType.STREAM_INVOCATION.value;

    public StreamInvocationMessage(String invocationId, String target, Object[] arguments) {
        super(target, arguments);
        this.invocationId = invocationId;
    }

    @Override
    public HubMessageType getMessageType() {
        return HubMessageType.STREAM_INVOCATION;
    }
}
