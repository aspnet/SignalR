// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

import { ILogger } from "../src/ILogger";
import { TransferFormat } from "../src/ITransport";
import { NullLogger } from "../src/Loggers";
import { WebSocketTransport } from "../src/WebSocketTransport";
import { VerifyLogger } from "./Common";
import { TestMessageEvent } from "./TestEventSource";
import { TestCloseEvent, TestEvent, TestWebSocket } from "./TestWebSocket";

describe("WebSocketTransport", () => {
    [[TransferFormat.Text, "blob"],
    [TransferFormat.Binary, "arraybuffer"]]
        .forEach(([input, expected]) => {
            it(`sets websocket binarytype based on transferformat: ${TransferFormat[input]}`, async () => {
                await VerifyLogger.run(async (logger) => {
                    const webSocket = new WebSocketTransport(undefined, logger, true, TestWebSocket);

                    const connectPromise = webSocket.connect("http://example.com", input as TransferFormat);

                    await TestWebSocket.webSocket.openSet;
                    TestWebSocket.webSocket.onopen(new TestEvent());

                    await connectPromise;

                    expect(TestWebSocket.webSocket.binaryType).toEqual(expected);
                });
            });
        });

    it("connect waits for WebSocket to be connected", async () => {
        const webSocket = new WebSocketTransport(undefined, NullLogger.instance, true, TestWebSocket);

        let connectComplete: boolean = false;
        const connectPromise = (async () => {
            await webSocket.connect("http://example.com", TransferFormat.Text);
            connectComplete = true;
        })();

        await TestWebSocket.webSocket.openSet;

        expect(connectComplete).toEqual(false);

        TestWebSocket.webSocket.onopen(new TestEvent());

        await connectPromise;
        expect(connectComplete).toEqual(true);
    });

    it("connect fails if there is error during connect", async () => {
        (global as any).ErrorEvent = TestEvent;
        const webSocket = new WebSocketTransport(undefined, NullLogger.instance, true, TestWebSocket);

        let connectComplete: boolean = false;
        const connectPromise = (async () => {
            await webSocket.connect("http://example.com", TransferFormat.Text);
            connectComplete = true;
        })();

        await TestWebSocket.webSocket.openSet;

        expect(connectComplete).toEqual(false);

        TestWebSocket.webSocket.onerror(new TestEvent());

        await expect(connectPromise)
            .rejects;
        expect(connectComplete).toEqual(false);
    });

    [["http://example.com", "ws://example.com?access_token=secretToken"],
    ["http://example.com?value=null", "ws://example.com?value=null&access_token=secretToken"]]
        .forEach(([input, expected]) => {
            it(`appends access_token to url ${input}`, async () => {
                await VerifyLogger.run(async (logger) => {
                    await createAndStartWebSocket(logger, input, () => "secretToken");

                    expect(TestWebSocket.webSocket.url).toEqual(expected);
                });
            });
        });

    it("can receive data", async () => {
        await VerifyLogger.run(async (logger) => {
            const webSocket = await createAndStartWebSocket(logger);

            let received: string | ArrayBuffer;
            webSocket.onreceive = (data) => {
                received = data;
            };

            const message = new TestMessageEvent();
            message.data = "receive data";
            TestWebSocket.webSocket.onmessage(message);

            expect(typeof received!).toEqual("string");
            expect(received!).toEqual("receive data");
        });
    });

    it("is closed from WebSocket onclose with error", async () => {
        await VerifyLogger.run(async (logger) => {
            (global as any).ErrorEvent = TestEvent;
            const webSocket = await createAndStartWebSocket(logger);

            let closeCalled: boolean = false;
            let error: Error;
            webSocket.onclose = (e) => {
                closeCalled = true;
                error = e!;
            };

            const message = new TestCloseEvent();
            message.wasClean = false;
            message.code = 1;
            message.reason = "just cause";
            TestWebSocket.webSocket.onclose(message);

            expect(closeCalled).toEqual(true);
            expect(error!).toEqual(new Error("Websocket closed with status code: 1 (just cause)"));

            await expect(webSocket.send(""))
                .rejects
                .toThrow("WebSocket is not in the OPEN state");
        });
    });

    it("is closed from WebSocket onclose", async () => {
        await VerifyLogger.run(async (logger) => {
            (global as any).ErrorEvent = TestEvent;
            const webSocket = await createAndStartWebSocket(logger);

            let closeCalled: boolean = false;
            let error: Error;
            webSocket.onclose = (e) => {
                closeCalled = true;
                error = e!;
            };

            const message = new TestCloseEvent();
            message.wasClean = true;
            message.code = 1000;
            message.reason = "success";
            TestWebSocket.webSocket.onclose(message);

            expect(closeCalled).toEqual(true);
            expect(error!).toEqual(undefined);

            await expect(webSocket.send(""))
                .rejects
                .toThrow("WebSocket is not in the OPEN state");
        });
    });

    it("is closed from Transport stop", async () => {
        await VerifyLogger.run(async (logger) => {
            (global as any).ErrorEvent = TestEvent;
            const webSocket = await createAndStartWebSocket(logger);

            let closeCalled: boolean = false;
            let error: Error;
            webSocket.onclose = (e) => {
                closeCalled = true;
                error = e!;
            };

            await webSocket.stop();

            expect(closeCalled).toEqual(true);
            expect(error!).toEqual(undefined);

            await expect(webSocket.send(""))
                .rejects
                .toThrow("WebSocket is not in the OPEN state");
        });
    });

    it("can send data", async () => {
        await VerifyLogger.run(async (logger) => {
            const webSocket = await createAndStartWebSocket(logger);

            TestWebSocket.webSocket.readyState = TestWebSocket.OPEN;
            await webSocket.send("send data");

            expect(TestWebSocket.webSocket.receivedData.length).toEqual(1);
            expect(TestWebSocket.webSocket.receivedData[0]).toEqual("send data");
        });
    });
});

async function createAndStartWebSocket(logger: ILogger, url?: string, accessTokenFactory?: (() => string | Promise<string>)): Promise<WebSocketTransport> {
    const webSocket = new WebSocketTransport(accessTokenFactory, logger, true, TestWebSocket);

    const connectPromise = webSocket.connect(url || "http://example.com", TransferFormat.Text);

    await TestWebSocket.webSocket.openSet;
    TestWebSocket.webSocket.onopen(new TestEvent());

    await connectPromise;

    return webSocket;
}
