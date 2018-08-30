// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

import { WebSocketWrapper } from "@aspnet/signalr/dist/esm/Polyfills";
import { ECHOENDPOINT_URL } from "./Common";

// On slower CI machines, these tests sometimes take longer than 5s
jasmine.DEFAULT_TIMEOUT_INTERVAL = 10 * 1000;

describe("WebSockets", () => {
    it("can be used to connect to SignalR", (done) => {
        const message = "message";

        let webSocket: WebSocket;
        if (typeof WebSocket !== "undefined") {
            webSocket = new WebSocket(ECHOENDPOINT_URL.replace(/^http/, "ws"));
        } else {
            webSocket = new WebSocketWrapper(ECHOENDPOINT_URL.replace(/^http/, "ws"));
        }

        webSocket.onopen = () => {
            webSocket.send(message);
        };

        webSocket.onmessage = (event) => {
            expect(event.data).toEqual(message);
            webSocket.close();
        };

        webSocket.onclose = (event) => {
            expect(event.code).toEqual(1000);
            expect(event.wasClean).toBe(true);

            done();
        };
    });
});
