// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

import { ConnectionClosed, DataReceived } from "../src/Common";
import { HubConnection } from "../src/HubConnection";
import { IConnection } from "../src/IConnection";
import { MessageType } from "../src/IHubProtocol";
import { ILogger, LogLevel } from "../src/ILogger";
import { Observer } from "../src/Observable";
import { TextMessageFormat } from "../src/TextMessageFormat";
import { ITransport, TransferFormat, TransportType } from "../src/Transports";

import { IHubConnectionOptions } from "../src/HubConnection";
import { asyncit as it, captureException, delay, PromiseSource } from "./Utils";

const commonOptions: IHubConnectionOptions = {
    logger: null,
};

describe("HubConnection", () => {

    describe("start", () => {
        it("sends negotiation message", async () => {
            const connection = new TestConnection();
            const hubConnection = new HubConnection(connection, commonOptions);
            await hubConnection.start();
            expect(connection.sentData.length).toBe(1);
            expect(JSON.parse(connection.sentData[0])).toEqual({
                protocol: "json",
            });
            await hubConnection.stop();
        });
    });

    describe("send", () => {
        it("sends a non blocking invocation", async () => {
            const connection = new TestConnection();

            const hubConnection = new HubConnection(connection, commonOptions);
            const invokePromise = hubConnection.send("testMethod", "arg", 42)
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

            // Close the connection
            hubConnection.stop();
        });
    });

    describe("invoke", () => {
        it("sends an invocation", async () => {
            const connection = new TestConnection();

            const hubConnection = new HubConnection(connection, commonOptions);
            const invokePromise = hubConnection.invoke("testMethod", "arg", 42)
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

            // Close the connection
            hubConnection.stop();
        });

        it("rejects the promise when an error is received", async () => {
            const connection = new TestConnection();

            const hubConnection = new HubConnection(connection, commonOptions);
            const invokePromise = hubConnection.invoke("testMethod", "arg", 42);

            connection.receive({ type: MessageType.Completion, invocationId: connection.lastInvocationId, error: "foo" });

            const ex = await captureException(async () => invokePromise);
            expect(ex.message).toBe("foo");
        });

        it("resolves the promise when a result is received", async () => {
            const connection = new TestConnection();

            const hubConnection = new HubConnection(connection, commonOptions);
            const invokePromise = hubConnection.invoke("testMethod", "arg", 42);

            connection.receive({ type: MessageType.Completion, invocationId: connection.lastInvocationId, result: "foo" });

            expect(await invokePromise).toBe("foo");
        });

        it("completes pending invocations when stopped", async () => {
            const connection = new TestConnection();

            const hubConnection = new HubConnection(connection, commonOptions);
            const invokePromise = hubConnection.invoke("testMethod");
            hubConnection.stop();

            const ex = await captureException(async () => await invokePromise);
            expect(ex.message).toBe("Invocation canceled due to connection being closed.");
        });

        it("completes pending invocations when connection is lost", async () => {
            const connection = new TestConnection();

            const hubConnection = new HubConnection(connection, commonOptions);
            const invokePromise = hubConnection.invoke("testMethod");
            // Typically this would be called by the transport
            connection.onclose(new Error("Connection lost"));

            const ex = await captureException(async () => await invokePromise);
            expect(ex.message).toBe("Connection lost");
        });
    });

    describe("on", () => {
        it("invocations ignored in callbacks not registered", async () => {
            const warnings: string[] = [];
            const logger = {
                log: (logLevel: LogLevel, message: string) => {
                    if (logLevel === LogLevel.Warning) {
                        warnings.push(message);
                    }
                },
            } as ILogger;
            const connection = new TestConnection();
            const hubConnection = new HubConnection(connection, { logger });

            connection.receive({
                arguments: ["test"],
                invocationId: 0,
                nonblocking: true,
                target: "message",
                type: MessageType.Invocation,
            });

            expect(warnings).toEqual(["No client method with the name 'message' found."]);
        });
        
        it("invocations ignored in callbacks that have registered then unregistered", async () => {
            const warnings: string[] = [];
            const logger = {
                log: (logLevel: LogLevel, message: string) => {
                    if (logLevel === LogLevel.Warning) {
                        warnings.push(message);
                    }
                },
            } as ILogger;
            const connection = new TestConnection();
            const hubConnection = new HubConnection(connection, { logger });

            const handler = () => { };
            hubConnection.on('message', handler);
            hubConnection.off('message', handler);

            connection.receive({
                arguments: ["test"],
                invocationId: 0,
                nonblocking: true,
                target: "message",
                type: MessageType.Invocation,
            });

            expect(warnings).toEqual(["No client method with the name 'message' found."]);
        });

        it("callback invoked when servers invokes a method on the client", async () => {
            const connection = new TestConnection();
            const hubConnection = new HubConnection(connection, commonOptions);
            let value = "";
            hubConnection.on("message", (v) => value = v);

            connection.receive({
                arguments: ["test"],
                invocationId: 0,
                nonblocking: true,
                target: "message",
                type: MessageType.Invocation,
            });

            expect(value).toBe("test");
        });

        it("can have multiple callbacks", async () => {
            const connection = new TestConnection();
            const hubConnection = new HubConnection(connection, commonOptions);
            let numInvocations1 = 0;
            let numInvocations2 = 0;
            hubConnection.on("message", () => numInvocations1++);
            hubConnection.on("message", () => numInvocations2++);

            connection.receive({
                arguments: [],
                invocationId: 0,
                nonblocking: true,
                target: "message",
                type: MessageType.Invocation,
            });

            expect(numInvocations1).toBe(1);
            expect(numInvocations2).toBe(1);
        });

        it("can unsubscribe from on", async () => {
            const connection = new TestConnection();
            const hubConnection = new HubConnection(connection, commonOptions);

            let numInvocations = 0;
            const callback = () => numInvocations++;
            hubConnection.on("message", callback);

            connection.receive({
                arguments: [],
                invocationId: 0,
                nonblocking: true,
                target: "message",
                type: MessageType.Invocation,
            });

            hubConnection.off("message", callback);

            connection.receive({
                arguments: [],
                invocationId: 0,
                nonblocking: true,
                target: "message",
                type: MessageType.Invocation,
            });

            expect(numInvocations).toBe(1);
        });

        it("unsubscribing from non-existing callbacks no-ops", async () => {
            const connection = new TestConnection();
            const hubConnection = new HubConnection(connection, commonOptions);

            hubConnection.off("_", () => { });
            hubConnection.on("message", (t) => { });
            hubConnection.on("message", () => { });
        });

        it("using null/undefined for methodName or method no-ops", async () => {
            const warnings: string[] = [];
            const logger = {
                log(logLevel: LogLevel, message: string) {
                    if (logLevel === LogLevel.Warning) {
                        warnings.push(message);
                    }

                },
            } as ILogger;

            const connection = new TestConnection();
            const hubConnection = new HubConnection(connection, { logger });

            hubConnection.on(null, undefined);
            hubConnection.on(undefined, null);
            hubConnection.on("message", null);
            hubConnection.on("message", undefined);
            hubConnection.on(null, () => { });
            hubConnection.on(undefined, () => { });

            // invoke a method to make sure we are not trying to use null/undefined
            connection.receive({
                arguments: [],
                invocationId: 0,
                nonblocking: true,
                target: "message",
                type: MessageType.Invocation,
            });

            expect(warnings).toEqual(["No client method with the name 'message' found."]);

            hubConnection.off(null, undefined);
            hubConnection.off(undefined, null);
            hubConnection.off("message", null);
            hubConnection.off("message", undefined);
            hubConnection.off(null, () => { });
            hubConnection.off(undefined, () => { });
        });
    });

    describe("stream", () => {
        it("sends an invocation", async () => {
            const connection = new TestConnection();

            const hubConnection = new HubConnection(connection, commonOptions);
            const invokePromise = hubConnection.stream("testStream", "arg", 42);

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
            hubConnection.stop();
        });

        it("completes with an error when an error is yielded", async () => {
            const connection = new TestConnection();

            const hubConnection = new HubConnection(connection, commonOptions);
            const observer = new TestObserver();
            hubConnection.stream<any>("testMethod", "arg", 42)
                .subscribe(observer);

            connection.receive({ type: MessageType.Completion, invocationId: connection.lastInvocationId, error: "foo" });

            const ex = await captureException(async () => await observer.completed);
            expect(ex.message).toEqual("Error: foo");
        });

        it("completes the observer when a completion is received", async () => {
            const connection = new TestConnection();

            const hubConnection = new HubConnection(connection, commonOptions);
            const observer = new TestObserver();
            hubConnection.stream<any>("testMethod", "arg", 42)
                .subscribe(observer);

            connection.receive({ type: MessageType.Completion, invocationId: connection.lastInvocationId });

            expect(await observer.completed).toEqual([]);
        });

        it("completes pending streams when stopped", async () => {
            const connection = new TestConnection();

            const hubConnection = new HubConnection(connection, commonOptions);
            const observer = new TestObserver();
            hubConnection.stream<any>("testMethod")
                .subscribe(observer);
            hubConnection.stop();

            const ex = await captureException(async () => await observer.completed);
            expect(ex.message).toEqual("Error: Invocation canceled due to connection being closed.");
        });

        it("completes pending streams when connection is lost", async () => {
            const connection = new TestConnection();

            const hubConnection = new HubConnection(connection, commonOptions);
            const observer = new TestObserver();
            hubConnection.stream<any>("testMethod")
                .subscribe(observer);

            // Typically this would be called by the transport
            connection.onclose(new Error("Connection lost"));

            const ex = await captureException(async () => await observer.completed);
            expect(ex.message).toEqual("Error: Connection lost");
        });

        it("yields items as they arrive", async () => {
            const connection = new TestConnection();

            const hubConnection = new HubConnection(connection, commonOptions);
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
        });

        it("does not require error function registered", async () => {
            const connection = new TestConnection();

            const hubConnection = new HubConnection(connection, commonOptions);
            const observer = hubConnection.stream("testMethod").subscribe({
                next: (val) => { },
            });

            // Typically this would be called by the transport
            // triggers observer.error()
            connection.onclose(new Error("Connection lost"));
        });

        it("does not require complete function registered", async () => {
            const connection = new TestConnection();

            const hubConnection = new HubConnection(connection, commonOptions);
            const observer = hubConnection.stream("testMethod").subscribe({
                next: (val) => { },
            });

            // Send completion to trigger observer.complete()
            // Expectation is connection.receive will not to throw
            connection.receive({ type: MessageType.Completion, invocationId: connection.lastInvocationId });
        });

        it("can be canceled", () => {
            const connection = new TestConnection();

            const hubConnection = new HubConnection(connection, commonOptions);
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
        });
    });

    describe("onClose", () => {
        it("can have multiple callbacks", async () => {
            const connection = new TestConnection();
            const hubConnection = new HubConnection(connection, commonOptions);
            let invocations = 0;
            hubConnection.onclose((e) => invocations++);
            hubConnection.onclose((e) => invocations++);
            // Typically this would be called by the transport
            connection.onclose();
            expect(invocations).toBe(2);
        });

        it("callbacks receive error", async () => {
            const connection = new TestConnection();
            const hubConnection = new HubConnection(connection, commonOptions);
            let error: Error;
            hubConnection.onclose((e) => error = e);

            // Typically this would be called by the transport
            connection.onclose(new Error("Test error."));
            expect(error.message).toBe("Test error.");
        });

        it("ignores null callbacks", async () => {
            const connection = new TestConnection();
            const hubConnection = new HubConnection(connection, commonOptions);
            hubConnection.onclose(null);
            hubConnection.onclose(undefined);
            // Typically this would be called by the transport
            connection.onclose();
            // expect no errors
        });
    });

    describe("keepAlive", () => {
        it("can receive ping messages", async () => {
            // Receive the ping mid-invocation so we can see that the rest of the flow works fine

            const connection = new TestConnection();
            const hubConnection = new HubConnection(connection, commonOptions);
            const invokePromise = hubConnection.invoke("testMethod", "arg", 42);

            connection.receive({ type: MessageType.Ping });
            connection.receive({ type: MessageType.Completion, invocationId: connection.lastInvocationId, result: "foo" });

            expect(await invokePromise).toBe("foo");
        });

        it("does not terminate if messages are received", async () => {
            const connection = new TestConnection();
            const hubConnection = new HubConnection(connection, { ...commonOptions, timeoutInMilliseconds: 100 });

            const p = new PromiseSource<Error>();
            hubConnection.onclose((e) => p.resolve(e));

            await hubConnection.start();

            await connection.receive({ type: MessageType.Ping });
            await delay(50);
            await connection.receive({ type: MessageType.Ping });
            await delay(50);
            await connection.receive({ type: MessageType.Ping });
            await delay(50);
            await connection.receive({ type: MessageType.Ping });
            await delay(50);

            connection.stop();

            const error = await p.promise;

            expect(error).toBeUndefined();
        });

        it("terminates if no messages received within timeout interval", async () => {
            const connection = new TestConnection();
            const hubConnection = new HubConnection(connection, { ...commonOptions, timeoutInMilliseconds: 100 });

            const p = new PromiseSource<Error>();
            hubConnection.onclose((e) => p.resolve(e));

            await hubConnection.start();

            const error = await p.promise;

            expect(error).toEqual(new Error("Server timeout elapsed without receiving a message from the server."));
        });
    });
});

class TestConnection implements IConnection {
    public readonly features: any = {};

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

    public receive(data: any): void {
        const payload = JSON.stringify(data);
        this.onreceive(TextMessageFormat.write(payload));
    }

    public onreceive: DataReceived;
    public onclose: ConnectionClosed;
    public sentData: any[];
    public lastInvocationId: string;
}

class TestObserver implements Observer<any> {
    public itemsReceived: [any];
    private itemsSource: PromiseSource<[any]>;

    get completed(): Promise<[any]> {
        return this.itemsSource.promise;
    }

    constructor() {
        this.itemsReceived = [] as [any];
        this.itemsSource = new PromiseSource<[any]>();
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
