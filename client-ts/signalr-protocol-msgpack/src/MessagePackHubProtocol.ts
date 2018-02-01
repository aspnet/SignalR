// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

import { IHubProtocol, ProtocolType, MessageType, HubMessage, InvocationMessage, ResultMessage, CompletionMessage, StreamInvocationMessage, MessageHeaders } from "@aspnet/signalr";
import { BinaryMessageFormat } from "./BinaryMessageFormat"
import { Buffer } from 'buffer';
import * as msgpack5 from "msgpack5";

export class MessagePackHubProtocol implements IHubProtocol {

    readonly name: string = "messagepack";

    readonly type: ProtocolType = ProtocolType.Binary;

    parseMessages(input: ArrayBuffer): HubMessage[] {
        return BinaryMessageFormat.parse(input).map(m => this.parseMessage(m));
    }

    private parseMessage(input: Uint8Array): HubMessage {
        if (input.length == 0) {
            throw new Error("Invalid payload.");
        }

        let msgpack = msgpack5();
        let properties = msgpack.decode(new Buffer(input));
        if (properties.length == 0 || !(properties instanceof Array)) {
            throw new Error("Invalid payload.");
        }

        let headers: MessageHeaders = properties[0] as MessageHeaders;
        if(typeof headers !== "object") {
            throw new Error("Invalid headers.");
        }
        let messageType = properties[1] as MessageType;

        switch (messageType) {
            case MessageType.Invocation:
                return this.createInvocationMessage(headers, properties);
            case MessageType.StreamItem:
                return this.createStreamItemMessage(headers, properties);
            case MessageType.Completion:
                return this.createCompletionMessage(headers, properties);
            case MessageType.Ping:
                return this.createPingMessage(properties);
            default:
                throw new Error("Invalid message type.");
        }
    }

    private createPingMessage(properties: any[]): HubMessage {
        if (properties.length != 2) {
            throw new Error("Invalid payload for Ping message.");
        }

        return {
            // Ping messages have no headers.
            type: MessageType.Ping
        } as HubMessage;
    }

    private createInvocationMessage(headers: MessageHeaders, properties: any[]): InvocationMessage {
        if (properties.length != 5) {
            throw new Error("Invalid payload for Invocation message.");
        }

        let invocationId = properties[2] as string;
        if (invocationId) {
            return {
                headers,
                type: MessageType.Invocation,
                invocationId: invocationId,
                target: properties[3] as string,
                arguments: properties[4],
            };
        }
        else {
            return {
                headers,
                type: MessageType.Invocation,
                target: properties[3],
                arguments: properties[4]
            };
        }

    }

    private createStreamItemMessage(headers: MessageHeaders, properties: any[]): ResultMessage {
        if (properties.length != 4) {
            throw new Error("Invalid payload for stream Result message.");
        }

        return {
            headers,
            type: MessageType.StreamItem,
            invocationId: properties[2],
            item: properties[3]
        } as ResultMessage;
    }

    private createCompletionMessage(headers: MessageHeaders, properties: any[]): CompletionMessage {
        if (properties.length < 4) {
            throw new Error("Invalid payload for Completion message.");
        }

        const errorResult = 1;
        const voidResult = 2;
        const nonVoidResult = 3;

        let resultKind = properties[3];

        if ((resultKind === voidResult && properties.length != 4) ||
            (resultKind !== voidResult && properties.length != 5)) {
            throw new Error("Invalid payload for Completion message.");
        }

        let completionMessage = {
            headers,
            type: MessageType.Completion,
            invocationId: properties[2],
            error: null as string,
            result: null as any
        };

        switch (resultKind) {
            case errorResult:
                completionMessage.error = properties[4];
                break;
            case nonVoidResult:
                completionMessage.result = properties[4];
                break;
        }

        return completionMessage as CompletionMessage;
    }

    writeMessage(message: HubMessage): ArrayBuffer {
        switch (message.type) {
            case MessageType.Invocation:
                return this.writeInvocation(message as InvocationMessage);
            case MessageType.StreamInvocation:
                return this.writeStreamInvocation(message as StreamInvocationMessage);
            case MessageType.StreamItem:
            case MessageType.Completion:
                throw new Error(`Writing messages of type '${message.type}' is not supported.`);
            default:
                throw new Error("Invalid message type.");
        }
    }

    private writeInvocation(invocationMessage: InvocationMessage): ArrayBuffer {
        let msgpack = msgpack5();
        let payload = msgpack.encode([invocationMessage.headers || {}, MessageType.Invocation, invocationMessage.invocationId || null,
        invocationMessage.target, invocationMessage.arguments]);

        return BinaryMessageFormat.write(payload.slice());
    }

    private writeStreamInvocation(streamInvocationMessage: StreamInvocationMessage): ArrayBuffer {
        let msgpack = msgpack5();
        let payload = msgpack.encode([streamInvocationMessage.headers || {}, MessageType.StreamInvocation, streamInvocationMessage.invocationId,
        streamInvocationMessage.target, streamInvocationMessage.arguments]);

        return BinaryMessageFormat.write(payload.slice());
    }
}