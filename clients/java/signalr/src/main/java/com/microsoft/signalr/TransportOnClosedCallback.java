package com.microsoft.signalr;

interface TransportOnClosedCallback {
    void invoke(String reason);
}
