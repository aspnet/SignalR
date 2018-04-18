// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

import { HubConnectionBuilder } from "../src/HubConnectionBuilder";

import { asyncit as it, PromiseSource } from "./Utils";
import { TestHttpClient } from "./TestHttpClient";
import { HttpRequest, HttpResponse } from "../src/HttpClient";
import { LogLevel, ILogger } from "../src/ILogger";
import { IHttpConnectionOptions } from "../src/HttpConnection";
import { NullLogger } from "../src/Loggers";
import { IHubProtocol, HubMessage } from "../src/IHubProtocol";
import { TransferFormat } from "../src/ITransport";
import { HubConnection } from "../src";

const negotiateResponse = {
    connectionId: "abc123",
    availableTransports: [
        { transport: "LongPolling", transferFormats: ["Text", "Binary"] }
    ]
};

const commonHttpOptions: IHttpConnectionOptions = {
    logMessageContent: true
}

function createConnectionBuilder(logger?: ILogger | LogLevel): HubConnectionBuilder {
    // We don't want to spam test output with logs. This can be changed as needed
    return new HubConnectionBuilder()
        .configureLogging(logger || NullLogger.instance);
}

function createTestClient(pollSent: PromiseSource<HttpRequest>, pollCompleted: Promise<HttpResponse>): TestHttpClient {
    let firstRequest = true;
    return new TestHttpClient()
        .on("POST", "http://example.com/negotiate", () => negotiateResponse)
        .on("GET", /http:\/\/example.com\?id=abc123&_=.*/, (req) => {
            if (firstRequest) {
                firstRequest = false;
                return new HttpResponse(200);
            } else {
                pollSent.resolve(req);
                return pollCompleted;
            }
        });
}

function makeClosedPromise(connection: HubConnection): Promise<void> {
    let closed = new PromiseSource();
    connection.onclose(error => {
        if (error) {
            closed.reject(error);
        } else {
            closed.resolve();
        }
    });
    return closed.promise;
}

describe("HubConnectionBuilder", () => {
    it("builds HubConnection with HttpConnection using provided URL", async () => {
        let pollSent = new PromiseSource<HttpRequest>();
        let pollCompleted = new PromiseSource<HttpResponse>();
        const testClient = createTestClient(pollSent, pollCompleted.promise)
            .on("POST", "http://example.com?id=abc123", (req) => {
                // Respond from the poll with the handshake response
                pollCompleted.resolve(new HttpResponse(204, "No Content", "{}"));
                return new HttpResponse(202);
            });
        const connection = createConnectionBuilder()
            .withUrl("http://example.com", {
                ...commonHttpOptions,
                httpClient: testClient
            })
            .build();

        // Start the connection
        const closed = makeClosedPromise(connection);
        await connection.start();

        const pollRequest = await pollSent.promise;
        expect(pollRequest.url).toMatch(/http:\/\/example.com\?id=abc123.*/);

        await closed;
    });

    it("can configure hub protocol", async () => {
        let protocol = new TestProtocol();

        let pollSent = new PromiseSource<HttpRequest>();
        let pollCompleted = new PromiseSource<HttpResponse>();
        let negotiateReceived = new PromiseSource<HttpRequest>();
        const testClient = createTestClient(pollSent, pollCompleted.promise)
            .on("POST", "http://example.com?id=abc123", (req) => {
                // Respond from the poll with the handshake response
                negotiateReceived.resolve(req);
                pollCompleted.resolve(new HttpResponse(204, "No Content", "{}"));
                return new HttpResponse(202);
            });

        const connection = createConnectionBuilder()
            .withUrl("http://example.com", {
                ...commonHttpOptions,
                httpClient: testClient
            })
            .withHubProtocol(protocol)
            .build();

        // Start the connection
        const closed = makeClosedPromise(connection);
        await connection.start();

        const negotiateRequest = await negotiateReceived.promise;
        expect(negotiateRequest.content).toBe(`{"protocol":"${protocol.name}","version":1}\x1E`);

        await closed;
    });


    it("allows logger to be replaced", async () => {
        let loggedMessages = 0;
        const logger = {
            log() {
                loggedMessages += 1;
            }
        }
        const connection = createConnectionBuilder(logger)
            .withUrl("http://example.com")
            .build();

        try {
            await connection.start();
        } catch {
            // Ignore failures
        }

        expect(loggedMessages).toBeGreaterThan(0);
    });

    it("uses logger for both HttpConnection and HubConnection", async () => {
        const logger = new CaptureLogger();
        const connection = createConnectionBuilder(logger)
            .withUrl("http://example.com")
            .build();

        try {
            await connection.start();
        } catch {
            // Ignore failures
        }

        // A HubConnection message
        expect(logger.messages).toContain("Starting HubConnection.");

        // An HttpConnection message
        expect(logger.messages).toContain("Starting connection with transfer format 'Text'.");
    });

    it("does not replace HttpConnectionOptions logger if provided", async () => {
        const hubConnectionLogger = new CaptureLogger();
        const httpConnectionLogger = new CaptureLogger();
        const connection = createConnectionBuilder(hubConnectionLogger)
            .withUrl("http://example.com", { logger: httpConnectionLogger })
            .build();

        try {
            await connection.start();
        } catch {
            // Ignore failures
        }

        // A HubConnection message
        expect(hubConnectionLogger.messages).toContain("Starting HubConnection.");
        expect(httpConnectionLogger.messages).not.toContain("Starting HubConnection.");

        // An HttpConnection message
        expect(httpConnectionLogger.messages).toContain("Starting connection with transfer format 'Text'.");
        expect(hubConnectionLogger.messages).not.toContain("Starting connection with transfer format 'Text'.");
    });
});

class CaptureLogger implements ILogger {
    public readonly messages: string[] = [];

    public log(logLevel: LogLevel, message: string): void {
        this.messages.push(message);
    }
}

class TestProtocol implements IHubProtocol {
    public name: string = "test";
    public version: number = 1;
    public transferFormat: TransferFormat = TransferFormat.Text;
    public parseMessages(input: any, logger: ILogger): HubMessage[] {
        throw new Error("Method not implemented.");
    }
    public writeMessage(message: HubMessage) {
        throw new Error("Method not implemented.");
    }
}