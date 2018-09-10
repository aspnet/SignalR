// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

package com.microsoft.aspnet.signalr;

public class StreamInvocationMessage extends HubMessage {

    int type = HubMessageType.STREAM_INVOCATION.value;
    String invocationId;
    String target;
    Object[] arguments;

    public StreamInvocationMessage(String invocationId, String target, Object[] arguments) {
        this.invocationId = invocationId;
        this.target = target;
        this.arguments = arguments;
    }

    @Override
    public HubMessageType getMessageType() {
        return HubMessageType.STREAM_INVOCATION;
    }

    public String getInvocationId() {
        return invocationId;
    }

    public void setInvocationId(String invocationId) {
        this.invocationId = invocationId;
    }

    public String getTarget() {
        return target;
    }

    public void setTarget(String target) {
        this.target = target;
    }

    public Object[] getArguments() {
        return arguments;
    }

    public void setArguments(Object[] arguments) {
        this.arguments = arguments;
    }
}
