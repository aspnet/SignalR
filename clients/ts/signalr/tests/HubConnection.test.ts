// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

import { HubConnection, HubConnectionState } from "../src/HubConnection";
import { IConnection } from "../src/IConnection";
import { HubMessage, IHubProtocol, MessageType } from "../src/IHubProtocol";
import { ILogger, LogLevel } from "../src/ILogger";
import { TransferFormat } from "../src/ITransport";
import { JsonHubProtocol } from "../src/JsonHubProtocol";
import { NullLogger } from "../src/Loggers";
import { IStreamSubscriber } from "../src/Stream";
import { TextMessageFormat } from "../src/TextMessageFormat";

import { VerifyLogger } from "./Common";
import { delay, PromiseSource, registerUnhandledRejectionHandler } from "./Utils";

function createHubConnection(connection: IConnection, logger?: ILogger | null, protocol?: IHubProtocol | null) {
    return HubConnection.create(connection, logger || NullLogger.instance, protocol || new JsonHubProtocol());
}

registerUnhandledRejectionHandler();

describe("HubConnection", () => {

    describe("start", () => {
        it("sends negotiation message", async () => {
            await VerifyLogger.run(async (logger) => {
                const connection = new TestConnection();
                const hubConnection = createHubConnection(connection, logger);
                try {
                    await hubConnection.start();
                    expect(connection.sentData.length).toBe(1);
                    expect(JSON.parse(connection.sentData[0])).toEqual({
                        protocol: "json",
                        version: 1,
                    });
                } finally {
                    await hubConnection.stop();
                }
            });
        });

        it("state connected", async () => {
            await VerifyLogger.run(async (logger) => {
                const connection = new TestConnection();
                const hubConnection = createHubConnection(connection, logger);
                expect(hubConnection.state).toBe(HubConnectionState.Disconnected);
                try {
                    await hubConnection.start();
                    expect(hubConnection.state).toBe(HubConnectionState.Connected);
                } finally {
                    await hubConnection.stop();
                }
            });
        });
    });

    describe("ping", () => {
        it("automatically sends multiple pings", async () => {
            await VerifyLogger.run(async (logger) => {
                const connection = new TestConnection();
                const hubConnection = createHubConnection(connection, logger);

                hubConnection.keepAliveIntervalInMilliseconds = 5;

                try {
                    await hubConnection.start();
                    await delay(500);

                    const numPings = connection.sentData.filter((s) => JSON.parse(s).type === MessageType.Ping).length;
                    expect(numPings).toBeGreaterThanOrEqual(2);
                } finally {
                    await hubConnection.stop();
                }
            });
        });
    });

    describe("stop", () => {
        it("state disconnected", async () => {
            await VerifyLogger.run(async (logger) => {
                const connection = new TestConnection();
                const hubConnection = createHubConnection(connection, logger);
                expect(hubConnection.state).toBe(HubConnectionState.Disconnected);
                try {
                    await hubConnection.start();
                    expect(hubConnection.state).toBe(HubConnectionState.Connected);
                } finally {
                    await hubConnection.stop();
                    expect(hubConnection.state).toBe(HubConnectionState.Disconnected);
                }
            });
        });
    });

    describe("send", () => {
        it("sends a non blocking invocation", async () => {
            await VerifyLogger.run(async (logger) => {
                const connection = new TestConnection();

                const hubConnection = createHubConnection(connection, logger);
                try {
                    // We don't actually care to wait for the send.
                    // tslint:disable-next-line:no-floating-promises
                    hubConnection.send("testMethod", "arg", 42)
                        .catch((_) => { }); // Suppress exception and unhandled promise rejection warning.

                    // Verify the message is sent
                    expect(connection.sentData.length).toBe(1);
                    expect(JSON.parse(connection.sentData[0])).toEqual({
                        arguments: [
                            "arg",
                            42,
                        ],
                        target: "testMethod",
                        type: MessageType.Invocation,
                    });
                } finally {
                    // Close the connection
                    await hubConnection.stop();
                }
            });
        });
    });

    describe("invoke", () => {
        it("sends an invocation", async () => {
            await VerifyLogger.run(async (logger) => {
                const connection = new TestConnection();

                const hubConnection = createHubConnection(connection, logger);
                try {
                    // We don't actually care to wait for the send.
                    // tslint:disable-next-line:no-floating-promises
                    hubConnection.invoke("testMethod", "arg", 42)
                        .catch((_) => { }); // Suppress exception and unhandled promise rejection warning.

                    // Verify the message is sent
                    expect(connection.sentData.length).toBe(1);
                    expect(JSON.parse(connection.sentData[0])).toEqual({
                        arguments: [
                            "arg",
                            42,
                        ],
                        invocationId: connection.lastInvocationId,
                        target: "testMethod",
                        type: MessageType.Invocation,
                    });

                } finally {
                    // Close the connection
                    await hubConnection.stop();
                }
            });
        });

        it("can process handshake from text", async () => {
            await VerifyLogger.run(async (logger) => {
                let protocolCalled = false;

                const mockProtocol = new TestProtocol(TransferFormat.Text);
                mockProtocol.onreceive = (d) => {
                    protocolCalled = true;
                };

                const connection = new TestConnection();
                const hubConnection = createHubConnection(connection, logger, mockProtocol);
                try {
                    const data = "{}" + TextMessageFormat.RecordSeparator;

                    connection.receiveText(data);

                    // message only contained handshake response
                    expect(protocolCalled).toEqual(false);
                } finally {
                    await hubConnection.stop();
                }
            });
        });

        it("can process handshake from binary", async () => {
            await VerifyLogger.run(async (logger) => {
                let protocolCalled = false;

                const mockProtocol = new TestProtocol(TransferFormat.Binary);
                mockProtocol.onreceive = (d) => {
                    protocolCalled = true;
                };

                const connection = new TestConnection();
                const hubConnection = createHubConnection(connection, logger, mockProtocol);
                try {
                    // handshake response + message separator
                    const data = [0x7b, 0x7d, 0x1e];

                    connection.receiveBinary(new Uint8Array(data).buffer);

                    // message only contained handshake response
                    expect(protocolCalled).toEqual(false);
                } finally {
                    await hubConnection.stop();
                }
            });
        });

        it("can process handshake and additional messages from binary", async () => {
            await VerifyLogger.run(async (logger) => {
                let receivedProcotolData: ArrayBuffer | undefined;

                const mockProtocol = new TestProtocol(TransferFormat.Binary);
                mockProtocol.onreceive = (d) => receivedProcotolData = d as ArrayBuffer;

                const connection = new TestConnection();
                const hubConnection = createHubConnection(connection, logger, mockProtocol);
                try {
                    // handshake response + message separator + message pack message
                    const data = [
                        0x7b, 0x7d, 0x1e, 0x65, 0x95, 0x03, 0x80, 0xa1, 0x30, 0x01, 0xd9, 0x5d, 0x54, 0x68, 0x65, 0x20, 0x63, 0x6c,
                        0x69, 0x65, 0x6e, 0x74, 0x20, 0x61, 0x74, 0x74, 0x65, 0x6d, 0x70, 0x74, 0x65, 0x64, 0x20, 0x74, 0x6f, 0x20,
                        0x69, 0x6e, 0x76, 0x6f, 0x6b, 0x65, 0x20, 0x74, 0x68, 0x65, 0x20, 0x73, 0x74, 0x72, 0x65, 0x61, 0x6d, 0x69,
                        0x6e, 0x67, 0x20, 0x27, 0x45, 0x6d, 0x70, 0x74, 0x79, 0x53, 0x74, 0x72, 0x65, 0x61, 0x6d, 0x27, 0x20, 0x6d,
                        0x65, 0x74, 0x68, 0x6f, 0x64, 0x20, 0x69, 0x6e, 0x20, 0x61, 0x20, 0x6e, 0x6f, 0x6e, 0x2d, 0x73, 0x74, 0x72,
                        0x65, 0x61, 0x6d, 0x69, 0x6e, 0x67, 0x20, 0x66, 0x61, 0x73, 0x68, 0x69, 0x6f, 0x6e, 0x2e,
                    ];

                    connection.receiveBinary(new Uint8Array(data).buffer);

                    // left over data is the message pack message
                    expect(receivedProcotolData!.byteLength).toEqual(102);
                } finally {
                    await hubConnection.stop();
                }
            });
        });

        it("can process handshake and additional messages from text", async () => {
            await VerifyLogger.run(async (logger) => {
                let receivedProcotolData: string | undefined;

                const mockProtocol = new TestProtocol(TransferFormat.Text);
                mockProtocol.onreceive = (d) => receivedProcotolData = d as string;

                const connection = new TestConnection();
                const hubConnection = createHubConnection(connection, logger, mockProtocol);
                try {
                    const data = "{}" + TextMessageFormat.RecordSeparator + "{\"type\":6}" + TextMessageFormat.RecordSeparator;

                    connection.receiveText(data);

                    expect(receivedProcotolData).toEqual("{\"type\":6}" + TextMessageFormat.RecordSeparator);
                } finally {
                    await hubConnection.stop();
                }
            });
        });

        it("rejects the promise when an error is received", async () => {
            await VerifyLogger.run(async (logger) => {
                const connection = new TestConnection();
                const hubConnection = createHubConnection(connection, logger);
                try {
                    connection.receiveHandshakeResponse();

                    const invokePromise = hubConnection.invoke("testMethod", "arg", 42);

                    connection.receive({ type: MessageType.Completion, invocationId: connection.lastInvocationId, error: "foo" });

                    await expect(invokePromise).rejects.toThrow("foo");
                } finally {
                    await hubConnection.stop();
                }
            });
        });

        it("resolves the promise when a result is received", async () => {
            await VerifyLogger.run(async (logger) => {
                const connection = new TestConnection();
                const hubConnection = createHubConnection(connection, logger);
                try {
                    connection.receiveHandshakeResponse();

                    const invokePromise = hubConnection.invoke("testMethod", "arg", 42);

                    connection.receive({ type: MessageType.Completion, invocationId: connection.lastInvocationId, result: "foo" });

                    expect(await invokePromise).toBe("foo");
                } finally {
                    await hubConnection.stop();
                }
            });
        });

        it("completes pending invocations when stopped", async () => {
            await VerifyLogger.run(async (logger) => {
                const connection = new TestConnection();

                const hubConnection = createHubConnection(connection, logger);

                connection.receiveHandshakeResponse();

                const invokePromise = hubConnection.invoke("testMethod");
                await hubConnection.stop();

                await expect(invokePromise).rejects.toThrow("Invocation canceled due to connection being closed.");
            });
        });

        it("completes pending invocations when connection is lost", async () => {
            await VerifyLogger.run(async (logger) => {
                const connection = new TestConnection();

                const hubConnection = createHubConnection(connection, logger);
                try {
                    connection.receiveHandshakeResponse();

                    const invokePromise = hubConnection.invoke("testMethod");
                    // Typically this would be called by the transport
                    connection.onclose!(new Error("Connection lost"));

                    await expect(invokePromise).rejects.toThrow("Connection lost");
                } finally {
                    await hubConnection.stop();
                }
            });
        });
    });

    describe("on", () => {
        it("invocations ignored in callbacks not registered", async () => {
            await VerifyLogger.run(async (logger) => {
                const warnings: string[] = [];
                const wrappingLogger = {
                    log: (logLevel: LogLevel, message: string) => {
                        if (logLevel === LogLevel.Warning) {
                            warnings.push(message);
                        }
                        logger.log(logLevel, message);
                    },
                } as ILogger;
                const connection = new TestConnection();
                const hubConnection = createHubConnection(connection, wrappingLogger);
                try {
                    connection.receiveHandshakeResponse();

                    connection.receive({
                        arguments: ["test"],
                        nonblocking: true,
                        target: "message",
                        type: MessageType.Invocation,
                    });

                    expect(warnings).toEqual(["No client method with the name 'message' found."]);
                } finally {
                    await hubConnection.stop();
                }
            });
        });

        it("invocations ignored in callbacks that have registered then unregistered", async () => {
            await VerifyLogger.run(async (logger) => {
                const warnings: string[] = [];
                const wrappingLogger = {
                    log: (logLevel: LogLevel, message: string) => {
                        if (logLevel === LogLevel.Warning) {
                            warnings.push(message);
                        }
                        logger.log(logLevel, message);
                    },
                } as ILogger;
                const connection = new TestConnection();
                const hubConnection = createHubConnection(connection, wrappingLogger);
                try {
                    connection.receiveHandshakeResponse();

                    const handler = () => { };
                    hubConnection.on("message", handler);
                    hubConnection.off("message", handler);

                    connection.receive({
                        arguments: ["test"],
                        invocationId: "0",
                        nonblocking: true,
                        target: "message",
                        type: MessageType.Invocation,
                    });

                    expect(warnings).toEqual(["No client method with the name 'message' found."]);
                } finally {
                    await hubConnection.stop();
                }
            });
        });

        it("all handlers can be unregistered with just the method name", async () => {
            await VerifyLogger.run(async (logger) => {
                const connection = new TestConnection();
                const hubConnection = createHubConnection(connection, logger);
                try {
                    connection.receiveHandshakeResponse();

                    let count = 0;
                    const handler = () => { count++; };
                    const secondHandler = () => { count++; };
                    hubConnection.on("inc", handler);
                    hubConnection.on("inc", secondHandler);

                    connection.receive({
                        arguments: [],
                        nonblocking: true,
                        target: "inc",
                        type: MessageType.Invocation,
                    });

                    hubConnection.off("inc");

                    connection.receive({
                        arguments: [],
                        nonblocking: true,
                        target: "inc",
                        type: MessageType.Invocation,
                    });

                    expect(count).toBe(2);
                } finally {
                    await hubConnection.stop();
                }
            });
        });

        it("a single handler can be unregistered with the method name and handler", async () => {
            await VerifyLogger.run(async (logger) => {
                const connection = new TestConnection();
                const hubConnection = createHubConnection(connection, logger);
                try {
                    connection.receiveHandshakeResponse();

                    let count = 0;
                    const handler = () => { count++; };
                    const secondHandler = () => { count++; };
                    hubConnection.on("inc", handler);
                    hubConnection.on("inc", secondHandler);

                    connection.receive({
                        arguments: [],
                        nonblocking: true,
                        target: "inc",
                        type: MessageType.Invocation,
                    });

                    hubConnection.off("inc", handler);

                    connection.receive({
                        arguments: [],
                        nonblocking: true,
                        target: "inc",
                        type: MessageType.Invocation,
                    });

                    expect(count).toBe(3);
                } finally {
                    await hubConnection.stop();
                }
            });
        });

        it("can't register the same handler multiple times", async () => {
            await VerifyLogger.run(async (logger) => {
                const connection = new TestConnection();
                const hubConnection = createHubConnection(connection, logger);
                try {
                    connection.receiveHandshakeResponse();

                    let count = 0;
                    const handler = () => { count++; };
                    hubConnection.on("inc", handler);
                    hubConnection.on("inc", handler);

                    connection.receive({
                        arguments: [],
                        nonblocking: true,
                        target: "inc",
                        type: MessageType.Invocation,
                    });

                    expect(count).toBe(1);
                } finally {
                    await hubConnection.stop();
                }
            });
        });

        it("callback invoked when servers invokes a method on the client", async () => {
            await VerifyLogger.run(async (logger) => {
                const connection = new TestConnection();
                const hubConnection = createHubConnection(connection, logger);
                try {
                    connection.receiveHandshakeResponse();

                    let value = "";
                    hubConnection.on("message", (v) => value = v);

                    connection.receive({
                        arguments: ["test"],
                        nonblocking: true,
                        target: "message",
                        type: MessageType.Invocation,
                    });

                    expect(value).toBe("test");
                } finally {
                    await hubConnection.stop();
                }
            });
        });

        it("stop on handshake error", async () => {
            await VerifyLogger.run(async (logger) => {
                const connection = new TestConnection();
                const hubConnection = createHubConnection(connection, logger);
                try {
                    let closeError: Error | undefined;
                    hubConnection.onclose((e) => closeError = e);

                    connection.receiveHandshakeResponse("Error!");

                    expect(closeError!.message).toEqual("Server returned handshake error: Error!");
                } finally {
                    await hubConnection.stop();
                }
            },
            "Server returned handshake error: Error!");
        });

        it("stop on close message", async () => {
            await VerifyLogger.run(async (logger) => {
                const connection = new TestConnection();
                const hubConnection = createHubConnection(connection, logger);
                try {
                    let isClosed = false;
                    let closeError: Error | undefined;
                    hubConnection.onclose((e) => {
                        isClosed = true;
                        closeError = e;
                    });

                    connection.receiveHandshakeResponse();

                    connection.receive({
                        type: MessageType.Close,
                    });

                    expect(isClosed).toEqual(true);
                    expect(closeError).toBeUndefined();
                } finally {
                    await hubConnection.stop();
                }
            });
        });

        it("stop on error close message", async () => {
            await VerifyLogger.run(async (logger) => {
                const connection = new TestConnection();
                const hubConnection = createHubConnection(connection, logger);
                try {
                    let isClosed = false;
                    let closeError: Error | undefined;
                    hubConnection.onclose((e) => {
                        isClosed = true;
                        closeError = e;
                    });

                    connection.receiveHandshakeResponse();

                    connection.receive({
                        error: "Error!",
                        type: MessageType.Close,
                    });

                    expect(isClosed).toEqual(true);
                    expect(closeError!.message).toEqual("Server returned an error on close: Error!");
                } finally {
                    await hubConnection.stop();
                }
            });
        });

        it("can have multiple callbacks", async () => {
            await VerifyLogger.run(async (logger) => {
                const connection = new TestConnection();
                const hubConnection = createHubConnection(connection, logger);
                try {
                    connection.receiveHandshakeResponse();

                    let numInvocations1 = 0;
                    let numInvocations2 = 0;
                    hubConnection.on("message", () => numInvocations1++);
                    hubConnection.on("message", () => numInvocations2++);

                    connection.receive({
                        arguments: [],
                        nonblocking: true,
                        target: "message",
                        type: MessageType.Invocation,
                    });

                    expect(numInvocations1).toBe(1);
                    expect(numInvocations2).toBe(1);
                } finally {
                    await hubConnection.stop();
                }
            });
        });

        it("can unsubscribe from on", async () => {
            await VerifyLogger.run(async (logger) => {
                const connection = new TestConnection();
                const hubConnection = createHubConnection(connection, logger);
                try {
                    connection.receiveHandshakeResponse();

                    let numInvocations = 0;
                    const callback = () => numInvocations++;
                    hubConnection.on("message", callback);

                    connection.receive({
                        arguments: [],
                        nonblocking: true,
                        target: "message",
                        type: MessageType.Invocation,
                    });

                    hubConnection.off("message", callback);

                    connection.receive({
                        arguments: [],
                        nonblocking: true,
                        target: "message",
                        type: MessageType.Invocation,
                    });

                    expect(numInvocations).toBe(1);
                } finally {
                    await hubConnection.stop();
                }
            });
        });

        it("unsubscribing from non-existing callbacks no-ops", async () => {
            await VerifyLogger.run(async (logger) => {
                const connection = new TestConnection();
                const hubConnection = createHubConnection(connection, logger);
                try {
                    hubConnection.off("_", () => { });
                    hubConnection.on("message", (t) => { });
                    hubConnection.on("message", () => { });
                } finally {
                    await hubConnection.stop();
                }
            });
        });

        it("using null/undefined for methodName or method no-ops", async () => {
            await VerifyLogger.run(async (logger) => {
                const warnings: string[] = [];
                const wrappingLogger = {
                    log(logLevel: LogLevel, message: string) {
                        if (logLevel === LogLevel.Warning) {
                            warnings.push(message);
                        }
                        logger.log(logLevel, message);
                    },
                } as ILogger;

                const connection = new TestConnection();
                const hubConnection = createHubConnection(connection, wrappingLogger);
                try {
                    connection.receiveHandshakeResponse();

                    hubConnection.on(null!, undefined!);
                    hubConnection.on(undefined!, null!);
                    hubConnection.on("message", null!);
                    hubConnection.on("message", undefined!);
                    hubConnection.on(null!, () => { });
                    hubConnection.on(undefined!, () => { });

                    // invoke a method to make sure we are not trying to use null/undefined
                    connection.receive({
                        arguments: [],
                        invocationId: "0",
                        nonblocking: true,
                        target: "message",
                        type: MessageType.Invocation,
                    });

                    expect(warnings).toEqual(["No client method with the name 'message' found."]);

                    hubConnection.off(null!, undefined!);
                    hubConnection.off(undefined!, null!);
                    hubConnection.off("message", null!);
                    hubConnection.off("message", undefined!);
                    hubConnection.off(null!, () => { });
                    hubConnection.off(undefined!, () => { });
                } finally {
                    await hubConnection.stop();
                }
            });
        });
    });

    describe("stream", () => {
        it("sends an invocation", async () => {
            await VerifyLogger.run(async (logger) => {
                const connection = new TestConnection();

                const hubConnection = createHubConnection(connection, logger);
                try {
                    hubConnection.stream("testStream", "arg", 42);

                    // Verify the message is sent
                    expect(connection.sentData.length).toBe(1);
                    expect(JSON.parse(connection.sentData[0])).toEqual({
                        arguments: [
                            "arg",
                            42,
                        ],
                        invocationId: connection.lastInvocationId,
                        target: "testStream",
                        type: MessageType.StreamInvocation,
                    });

                    // Close the connection
                    await hubConnection.stop();
                } finally {
                    await hubConnection.stop();
                }
            });
        });

        it("completes with an error when an error is yielded", async () => {
            await VerifyLogger.run(async (logger) => {
                const connection = new TestConnection();
                const hubConnection = createHubConnection(connection, logger);
                try {
                    connection.receiveHandshakeResponse();

                    const observer = new TestObserver();
                    hubConnection.stream<any>("testMethod", "arg", 42)
                        .subscribe(observer);

                    connection.receive({ type: MessageType.Completion, invocationId: connection.lastInvocationId, error: "foo" });

                    await expect(observer.completed).rejects.toThrow("Error: foo");
                } finally {
                    await hubConnection.stop();
                }
            });
        });

        it("completes the observer when a completion is received", async () => {
            await VerifyLogger.run(async (logger) => {
                const connection = new TestConnection();
                const hubConnection = createHubConnection(connection, logger);
                try {
                    connection.receiveHandshakeResponse();

                    const observer = new TestObserver();
                    hubConnection.stream<any>("testMethod", "arg", 42)
                        .subscribe(observer);

                    connection.receive({ type: MessageType.Completion, invocationId: connection.lastInvocationId });

                    expect(await observer.completed).toEqual([]);
                } finally {
                    await hubConnection.stop();
                }
            });
        });

        it("completes pending streams when stopped", async () => {
            await VerifyLogger.run(async (logger) => {
                const connection = new TestConnection();

                const hubConnection = createHubConnection(connection, logger);
                try {
                    const observer = new TestObserver();
                    hubConnection.stream<any>("testMethod")
                        .subscribe(observer);
                    await hubConnection.stop();

                    await expect(observer.completed).rejects.toThrow("Error: Invocation canceled due to connection being closed.");
                } finally {
                    await hubConnection.stop();
                }
            });
        });

        it("completes pending streams when connection is lost", async () => {
            await VerifyLogger.run(async (logger) => {
                const connection = new TestConnection();

                const hubConnection = createHubConnection(connection, logger);
                try {
                    const observer = new TestObserver();
                    hubConnection.stream<any>("testMethod")
                        .subscribe(observer);

                    // Typically this would be called by the transport
                    connection.onclose!(new Error("Connection lost"));

                    await expect(observer.completed).rejects.toThrow("Error: Connection lost");
                } finally {
                    await hubConnection.stop();
                }
            });
        });

        it("yields items as they arrive", async () => {
            await VerifyLogger.run(async (logger) => {
                const connection = new TestConnection();
                const hubConnection = createHubConnection(connection, logger);
                try {
                    connection.receiveHandshakeResponse();

                    const observer = new TestObserver();
                    hubConnection.stream<any>("testMethod")
                        .subscribe(observer);

                    connection.receive({ type: MessageType.StreamItem, invocationId: connection.lastInvocationId, item: 1 });
                    expect(observer.itemsReceived).toEqual([1]);

                    connection.receive({ type: MessageType.StreamItem, invocationId: connection.lastInvocationId, item: 2 });
                    expect(observer.itemsReceived).toEqual([1, 2]);

                    connection.receive({ type: MessageType.StreamItem, invocationId: connection.lastInvocationId, item: 3 });
                    expect(observer.itemsReceived).toEqual([1, 2, 3]);

                    connection.receive({ type: MessageType.Completion, invocationId: connection.lastInvocationId });
                    expect(await observer.completed).toEqual([1, 2, 3]);
                } finally {
                    await hubConnection.stop();
                }
            });
        });

        it("does not require error function registered", async () => {
            await VerifyLogger.run(async (logger) => {
                const connection = new TestConnection();

                const hubConnection = createHubConnection(connection, logger);
                try {
                    hubConnection.stream("testMethod").subscribe(NullSubscriber.instance);

                    // Typically this would be called by the transport
                    // triggers observer.error()
                    connection.onclose!(new Error("Connection lost"));
                } finally {
                    await hubConnection.stop();
                }
            });
        });

        it("does not require complete function registered", async () => {
            await VerifyLogger.run(async (logger) => {
                const connection = new TestConnection();

                const hubConnection = createHubConnection(connection, logger);
                try {
                    hubConnection.stream("testMethod").subscribe(NullSubscriber.instance);

                    // Send completion to trigger observer.complete()
                    // Expectation is connection.receive will not to throw
                    connection.receive({ type: MessageType.Completion, invocationId: connection.lastInvocationId });
                } finally {
                    await hubConnection.stop();
                }
            });
        });

        it("can be canceled", async () => {
            await VerifyLogger.run(async (logger) => {
                const connection = new TestConnection();
                const hubConnection = createHubConnection(connection, logger);
                try {
                    connection.receiveHandshakeResponse();

                    const observer = new TestObserver();
                    const subscription = hubConnection.stream("testMethod")
                        .subscribe(observer);

                    connection.receive({ type: MessageType.StreamItem, invocationId: connection.lastInvocationId, item: 1 });
                    expect(observer.itemsReceived).toEqual([1]);

                    subscription.dispose();

                    connection.receive({ type: MessageType.StreamItem, invocationId: connection.lastInvocationId, item: 2 });
                    // Observer should no longer receive messages
                    expect(observer.itemsReceived).toEqual([1]);

                    // Verify the cancel is sent
                    expect(connection.sentData.length).toBe(2);
                    expect(JSON.parse(connection.sentData[1])).toEqual({
                        invocationId: connection.lastInvocationId,
                        type: MessageType.CancelInvocation,
                    });
                } finally {
                    await hubConnection.stop();
                }
            });
        });
    });

    describe("onClose", () => {
        it("can have multiple callbacks", async () => {
            await VerifyLogger.run(async (logger) => {
                const connection = new TestConnection();
                const hubConnection = createHubConnection(connection, logger);
                try {
                    let invocations = 0;
                    hubConnection.onclose((e) => invocations++);
                    hubConnection.onclose((e) => invocations++);
                    // Typically this would be called by the transport
                    connection.onclose!();
                    expect(invocations).toBe(2);
                } finally {
                    await hubConnection.stop();
                }
            });
        });

        it("callbacks receive error", async () => {
            await VerifyLogger.run(async (logger) => {
                const connection = new TestConnection();
                const hubConnection = createHubConnection(connection, logger);
                try {
                    let error: Error | undefined;
                    hubConnection.onclose((e) => error = e);

                    // Typically this would be called by the transport
                    connection.onclose!(new Error("Test error."));
                    expect(error!.message).toBe("Test error.");
                } finally {
                    await hubConnection.stop();
                }
            });
        });

        it("ignores null callbacks", async () => {
            await VerifyLogger.run(async (logger) => {
                const connection = new TestConnection();
                const hubConnection = createHubConnection(connection, logger);
                try {
                    hubConnection.onclose(null!);
                    hubConnection.onclose(undefined!);
                    // Typically this would be called by the transport
                    connection.onclose!();
                    // expect no errors
                } finally {
                    await hubConnection.stop();
                }
            });
        });

        it("state disconnected", async () => {
            await VerifyLogger.run(async (logger) => {
                const connection = new TestConnection();
                const hubConnection = createHubConnection(connection, logger);
                try {
                    let state: HubConnectionState | undefined;
                    hubConnection.onclose((e) => state = hubConnection.state);
                    // Typically this would be called by the transport
                    connection.onclose!();

                    expect(state).toBe(HubConnectionState.Disconnected);
                } finally {
                    await hubConnection.stop();
                }
            });
        });
    });

    describe("keepAlive", () => {
        it("can receive ping messages", async () => {
            await VerifyLogger.run(async (logger) => {
                // Receive the ping mid-invocation so we can see that the rest of the flow works fine

                const connection = new TestConnection();
                const hubConnection = createHubConnection(connection, logger);
                try {
                    const invokePromise = hubConnection.invoke("testMethod", "arg", 42);

                    connection.receive({ type: MessageType.Ping });
                    connection.receive({ type: MessageType.Completion, invocationId: connection.lastInvocationId, result: "foo" });

                    expect(await invokePromise).toBe("foo");
                } finally {
                    await hubConnection.stop();
                }
            });
        });

        it("does not terminate if messages are received", async () => {
            await VerifyLogger.run(async (logger) => {
                const connection = new TestConnection();
                const hubConnection = createHubConnection(connection, logger);
                try {
                    hubConnection.serverTimeoutInMilliseconds = 200;

                    const p = new PromiseSource<Error>();
                    hubConnection.onclose((e) => p.resolve(e));

                    await hubConnection.start();

                    for (let i = 0; i < 6; i++) {
                        await pingAndWait(connection);
                    }

                    await connection.stop();

                    const error = await p.promise;

                    expect(error).toBeUndefined();
                } finally {
                    await hubConnection.stop();
                }
            });
        });

        it("does not timeout if message was received before HubConnection.start", async () => {
            await VerifyLogger.run(async (logger) => {
                const connection = new TestConnection();
                const hubConnection = createHubConnection(connection, logger);
                try {
                    hubConnection.serverTimeoutInMilliseconds = 200;

                    const p = new PromiseSource<Error>();
                    hubConnection.onclose((e) => p.resolve(e));

                    // send message before start to trigger timeout handler
                    // testing for regression where we didn't cleanup timer if request received before start created a timer
                    await connection.receive({ type: MessageType.Ping });

                    await hubConnection.start();

                    for (let i = 0; i < 6; i++) {
                        await pingAndWait(connection);
                    }

                    await connection.stop();

                    const error = await p.promise;

                    expect(error).toBeUndefined();
                } finally {
                    await hubConnection.stop();
                }
            });
        });

        it("terminates if no messages received within timeout interval", async () => {
            await VerifyLogger.run(async (logger) => {
                const connection = new TestConnection();
                const hubConnection = createHubConnection(connection, logger);
                try {
                    hubConnection.serverTimeoutInMilliseconds = 100;

                    const p = new PromiseSource<Error>();
                    hubConnection.onclose((e) => p.resolve(e));

                    await hubConnection.start();

                    const error = await p.promise;

                    expect(error).toEqual(new Error("Server timeout elapsed without receiving a message from the server."));
                } finally {
                    await hubConnection.stop();
                }
            });
        });
    });
});

async function pingAndWait(connection: TestConnection): Promise<void> {
    await connection.receive({ type: MessageType.Ping });
    await delay(50);
}

class TestConnection implements IConnection {
    public readonly features: any = {};

    public onreceive: ((data: string | ArrayBuffer) => void) | null;
    public onclose: ((error?: Error) => void) | null;
    public sentData: any[];
    public lastInvocationId: string | null;

    constructor() {
        this.onreceive = null;
        this.onclose = null;
        this.sentData = [];
        this.lastInvocationId = null;
    }

    public start(): Promise<void> {
        return Promise.resolve();
    }

    public send(data: any): Promise<void> {
        const invocation = TextMessageFormat.parse(data)[0];
        const invocationId = JSON.parse(invocation).invocationId;
        if (invocationId) {
            this.lastInvocationId = invocationId;
        }
        if (this.sentData) {
            this.sentData.push(invocation);
        } else {
            this.sentData = [invocation];
        }
        return Promise.resolve();
    }

    public stop(error?: Error): Promise<void> {
        if (this.onclose) {
            this.onclose(error);
        }
        return Promise.resolve();
    }

    public receiveHandshakeResponse(error?: string): void {
        this.receive({ error });
    }

    public receive(data: any): void {
        const payload = JSON.stringify(data);
        this.invokeOnReceive(TextMessageFormat.write(payload));
    }

    public receiveText(data: string) {
        this.invokeOnReceive(data);
    }

    public receiveBinary(data: ArrayBuffer) {
        this.invokeOnReceive(data);
    }

    private invokeOnReceive(data: string | ArrayBuffer) {
        if (this.onreceive) {
            this.onreceive(data);
        }
    }
}

class TestProtocol implements IHubProtocol {
    public readonly name: string = "TestProtocol";
    public readonly version: number = 1;

    public readonly transferFormat: TransferFormat;

    public onreceive: ((data: string | ArrayBuffer) => void) | null;

    constructor(transferFormat: TransferFormat) {
        this.transferFormat = transferFormat;
        this.onreceive = null;
    }

    public parseMessages(input: any): HubMessage[] {
        if (this.onreceive) {
            this.onreceive(input);
        }

        return [];
    }

    public writeMessage(message: HubMessage): any {

    }
}

class TestObserver implements IStreamSubscriber<any> {
    public readonly closed: boolean = false;
    public itemsReceived: any[];
    private itemsSource: PromiseSource<any[]>;

    get completed(): Promise<any[]> {
        return this.itemsSource.promise;
    }

    constructor() {
        this.itemsReceived = [];
        this.itemsSource = new PromiseSource<any[]>();
    }

    public next(value: any) {
        this.itemsReceived.push(value);
    }

    public error(err: any) {
        this.itemsSource.reject(new Error(err));
    }

    public complete() {
        this.itemsSource.resolve(this.itemsReceived);
    }
}

class NullSubscriber<T> implements IStreamSubscriber<T> {
    public static instance: NullSubscriber<any> = new NullSubscriber();

    private constructor() {
    }

    public next(value: T): void {
    }
    public error(err: any): void {
    }
    public complete(): void {
    }
}
