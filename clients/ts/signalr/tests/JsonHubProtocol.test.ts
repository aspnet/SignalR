// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

import { CompletionMessage, InvocationMessage, MessageType, StreamItemMessage } from "../src/IHubProtocol";
import { JsonHubProtocol } from "../src/JsonHubProtocol";
import { TextMessageFormat } from "../src/TextMessageFormat";
import { VerifyLogger } from "./Common";
import { registerUnhandledRejectionHandler } from "./Utils";

registerUnhandledRejectionHandler();

describe("JsonHubProtocol", () => {
    it("can write/read non-blocking Invocation message", async () => {
        await VerifyLogger.run(async (logger) => {
            const invocation = {
                arguments: [42, true, "test", ["x1", "y2"], null],
                headers: {},
                target: "myMethod",
                type: MessageType.Invocation,
            } as InvocationMessage;

            const protocol = new JsonHubProtocol();
            const parsedMessages = protocol.parseMessages(protocol.writeMessage(invocation), logger);
            expect(parsedMessages).toEqual([invocation]);
        });
    });

    it("can read Invocation message with Date argument", async () => {
        await VerifyLogger.run(async (logger) => {
            const invocation = {
                arguments: [Date.UTC(2018, 1, 1, 12, 34, 56)],
                headers: {},
                target: "mymethod",
                type: MessageType.Invocation,
            } as InvocationMessage;

            const protocol = new JsonHubProtocol();
            const parsedMessages = protocol.parseMessages(protocol.writeMessage(invocation), logger);
            expect(parsedMessages).toEqual([invocation]);
        });
    });

    it("can write/read Invocation message with headers", async () => {
        await VerifyLogger.run(async (logger) => {
            const invocation = {
                arguments: [42, true, "test", ["x1", "y2"], null],
                headers: {
                    foo: "bar",
                },
                target: "myMethod",
                type: MessageType.Invocation,
            } as InvocationMessage;

            const protocol = new JsonHubProtocol();
            const parsedMessages = protocol.parseMessages(protocol.writeMessage(invocation), logger);
            expect(parsedMessages).toEqual([invocation]);
        });
    });

    it("can write/read Invocation message", async () => {
        await VerifyLogger.run(async (logger) => {
            const invocation = {
                arguments: [42, true, "test", ["x1", "y2"], null],
                headers: {},
                invocationId: "123",
                target: "myMethod",
                type: MessageType.Invocation,
            } as InvocationMessage;

            const protocol = new JsonHubProtocol();
            const parsedMessages = protocol.parseMessages(protocol.writeMessage(invocation), logger);
            expect(parsedMessages).toEqual([invocation]);
        });
    });

    ([
        [`{"type":3, "invocationId": "abc", "error": "Err", "result": null, "headers": {}}${TextMessageFormat.RecordSeparator}`,
        {
            error: "Err",
            headers: {},
            invocationId: "abc",
            result: null,
            type: MessageType.Completion,
        } as CompletionMessage],
        [`{"type":3, "invocationId": "abc", "result": "OK", "headers": {}}${TextMessageFormat.RecordSeparator}`,
        {
            headers: {},
            invocationId: "abc",
            result: "OK",
            type: MessageType.Completion,
        } as CompletionMessage],
        [`{"type":3, "invocationId": "abc", "result": null, "headers": {}}${TextMessageFormat.RecordSeparator}`,
        {
            headers: {},
            invocationId: "abc",
            result: null,
            type: MessageType.Completion,
        } as CompletionMessage],
        [`{"type":3, "invocationId": "abc", "result": 1514805840000, "headers": {}}${TextMessageFormat.RecordSeparator}`,
        {
            headers: {},
            invocationId: "abc",
            result: Date.UTC(2018, 0, 1, 11, 24, 0),
            type: MessageType.Completion,
        } as CompletionMessage],
        [`{"type":3, "invocationId": "abc", "result": null, "headers": {}, "extraParameter":"value"}${TextMessageFormat.RecordSeparator}`,
        {
            extraParameter: "value",
            headers: {},
            invocationId: "abc",
            result: null,
            type: MessageType.Completion,
        } as CompletionMessage],
    ] as Array<[string, CompletionMessage]>).forEach(([payload, expectedMessage]) =>
        it("can read Completion message", async () => {
            await VerifyLogger.run(async (logger) => {
                const messages = new JsonHubProtocol().parseMessages(payload, logger);
                expect(messages).toEqual([expectedMessage]);
            });
        }));

    ([
        [`{"type":2, "invocationId": "abc", "headers": {}, "item": 8}${TextMessageFormat.RecordSeparator}`,
        {
            headers: {},
            invocationId: "abc",
            item: 8,
            type: MessageType.StreamItem,
        } as StreamItemMessage],
        [`{"type":2, "invocationId": "abc", "headers": {}, "item": 1514805840000}${TextMessageFormat.RecordSeparator}`,
        {
            headers: {},
            invocationId: "abc",
            item: Date.UTC(2018, 0, 1, 11, 24, 0),
            type: MessageType.StreamItem,
        } as StreamItemMessage],
    ] as Array<[string, StreamItemMessage]>).forEach(([payload, expectedMessage]) =>
        it("can read StreamItem message", async () => {
            await VerifyLogger.run(async (logger) => {
                const messages = new JsonHubProtocol().parseMessages(payload, logger);
                expect(messages).toEqual([expectedMessage]);
            });
        }));

    ([
        [`{"type":2, "invocationId": "abc", "headers": {"t": "u"}, "item": 8}${TextMessageFormat.RecordSeparator}`,
        {
            headers: {
                t: "u",
            },
            invocationId: "abc",
            item: 8,
            type: MessageType.StreamItem,
        } as StreamItemMessage],
    ] as Array<[string, StreamItemMessage]>).forEach(([payload, expectedMessage]) =>
        it("can read message with headers", async () => {
            await VerifyLogger.run(async (logger) => {
                const messages = new JsonHubProtocol().parseMessages(payload, logger);
                expect(messages).toEqual([expectedMessage]);
            });
        }));

    ([
        ["message with empty payload", `{}${TextMessageFormat.RecordSeparator}`, new Error("Invalid payload.")],
        ["Invocation message with invalid invocation id", `{"type":1,"invocationId":1,"target":"method"}${TextMessageFormat.RecordSeparator}`, "Invalid payload for Invocation message."],
        ["Invocation message with empty string invocation id", `{"type":1,"invocationId":"","target":"method"}${TextMessageFormat.RecordSeparator}`, "Invalid payload for Invocation message."],
        ["Invocation message with invalid target", `{"type":1,"invocationId":"1","target":1}${TextMessageFormat.RecordSeparator}`, "Invalid payload for Invocation message."],
        ["StreamItem message with missing invocation id", `{"type":2}${TextMessageFormat.RecordSeparator}`, "Invalid payload for StreamItem message."],
        ["StreamItem message with invalid invocation id", `{"type":2,"invocationId":1}${TextMessageFormat.RecordSeparator}`, "Invalid payload for StreamItem message."],
        ["Completion message with missing invocation id", `{"type":3}${TextMessageFormat.RecordSeparator}`, "Invalid payload for Completion message."],
        ["Completion message with invalid invocation id", `{"type":3,"invocationId":1}${TextMessageFormat.RecordSeparator}`, "Invalid payload for Completion message."],
        ["Completion message with result and error", `{"type":3,"invocationId":"1","result":2,"error":"error"}${TextMessageFormat.RecordSeparator}`, "Invalid payload for Completion message."],
        ["Completion message with non-string error", `{"type":3,"invocationId":"1","error":21}${TextMessageFormat.RecordSeparator}`, "Invalid payload for Completion message."],
    ] as Array<[string, string, string]>).forEach(([name, payload, expectedError]) =>
        it("throws for " + name, async () => {
            await VerifyLogger.run(async (logger) => {
                expect(() => new JsonHubProtocol().parseMessages(payload, logger))
                    .toThrow(expectedError);
            });
        }));

    it("can read multiple messages", async () => {
        await VerifyLogger.run(async (logger) => {
            const payload = `{"type":2, "invocationId": "abc", "headers": {}, "item": 8}${TextMessageFormat.RecordSeparator}{"type":3, "invocationId": "abc", "headers": {}, "result": "OK"}${TextMessageFormat.RecordSeparator}`;
            const messages = new JsonHubProtocol().parseMessages(payload, logger);
            expect(messages).toEqual([
                {
                    headers: {},
                    invocationId: "abc",
                    item: 8,
                    type: MessageType.StreamItem,
                } as StreamItemMessage,
                {
                    headers: {},
                    invocationId: "abc",
                    result: "OK",
                    type: MessageType.Completion,
                } as CompletionMessage,
            ]);
        });
    });

    it("can read ping message", async () => {
        await VerifyLogger.run(async (logger) => {
            const payload = `{"type":6}${TextMessageFormat.RecordSeparator}`;
            const messages = new JsonHubProtocol().parseMessages(payload, logger);
            expect(messages).toEqual([
                {
                    type: MessageType.Ping,
                },
            ]);
        });
    });
});
