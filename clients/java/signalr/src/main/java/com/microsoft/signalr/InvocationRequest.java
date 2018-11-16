// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

package com.microsoft.signalr;

import java.util.concurrent.CancellationException;

import io.reactivex.Single;
import io.reactivex.subjects.ReplaySubject;
import io.reactivex.subjects.SingleSubject;
import io.reactivex.subjects.Subject;

class InvocationRequest {
    private final Class<?> returnType;
    private final Subject<Object> pendingCall = ReplaySubject.create();
    private final String invocationId;

    InvocationRequest(Class<?> returnType, String invocationId) {
        this.returnType = returnType;
        this.invocationId = invocationId;
    }

    public void complete(CompletionMessage completion) {
        if (completion.getError() == null) {
            if(completion.getResult() != null){
                pendingCall.onNext(completion.getResult());
            }
            pendingCall.onComplete();
        } else {
            pendingCall.onError(new HubException(completion.getError()));
        }
    }

    public void addItem(StreamItem streamItem) {
        if(streamItem.getResult() != null) {
            pendingCall.onNext(streamItem.getResult());
        }
    }

    public void fail(Exception ex) {
        pendingCall.onError(ex);
    }

    public void cancel() {
        pendingCall.onError(new CancellationException("Invocation was canceled."));
    }

    public Subject<Object> getPendingCall() {
        return pendingCall;
    }

    public Class<?> getReturnType() {
        return returnType;
    }

    public String getInvocationId() {
        return invocationId;
    }
}