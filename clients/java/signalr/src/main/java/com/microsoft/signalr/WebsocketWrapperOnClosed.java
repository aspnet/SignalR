package com.microsoft.signalr;

interface WebsocketWrapperOnClosed {
    void invoke(Integer code, String string);
}
