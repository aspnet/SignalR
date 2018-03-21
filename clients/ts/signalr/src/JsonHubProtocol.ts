// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

import { HubMessage, IHubProtocol, MessageType } from "./IHubProtocol";
import { TextMessageFormat } from "./TextMessageFormat";
import { TransferFormat } from "./Transports";
import { InvocationMessage, StreamItemMessage, CompletionMessage, PingMessage, CloseMessage } from "./browser-index";

export const JSON_HUB_PROTOCOL_NAME: string = "json";

export class JsonHubProtocol implements IHubProtocol {

    public readonly name: string = JSON_HUB_PROTOCOL_NAME;
    public readonly version: number = 1;

    public readonly transferFormat: TransferFormat = TransferFormat.Text;

    public parseMessages(input: string): HubMessage[] {
        if (!input) {
            return [];
        }

        // Parse the messages
        const messages = TextMessageFormat.parse(input);
        console.log(messages);

        const hubMessages = [];
        for (const message of messages) {
            const parsedMessage = JSON.parse(message) as HubMessage;
            if (parsedMessage.type === undefined) {
                throw new Error("Invalid payload.");
            }
            switch (parsedMessage.type) {
                case MessageType.Invocation:
                    this.isInvocationMessage(parsedMessage);
                    break;
                case MessageType.StreamItem:
                    this.isStreamItemMessage(parsedMessage);
                    break;
                case MessageType.Completion:
                    this.isCompletionMessage(parsedMessage);
                    break;
                case MessageType.Ping:
                    // Single value, no need to validate
                    break;
                case MessageType.Close:
                    // All optional values, no need to validate
                    break;
                default:
                    throw new Error("Invalid message type.");
            }
            hubMessages.push(parsedMessage);
        }

        return hubMessages;
    }

    public writeMessage(message: HubMessage): string {
        return TextMessageFormat.write(JSON.stringify(message));
    }

    private isInvocationMessage(message: InvocationMessage): void {
        if (message.target === undefined) {
            throw new Error("Invalid payload for Invocation message.");
        }
    }

    private isStreamItemMessage(message: StreamItemMessage): void {
        if (message.invocationId === undefined) {
            throw new Error("Invalid payload for StreamItem message.");
        }
        if (message.item === undefined) {
            throw new Error("Invalid payload for StreamItem message.");
        }
    }

    private isCompletionMessage(message: CompletionMessage): void {
        if (message.result && message.error) {
            throw new Error("Invalid payload for Completion message.");
        }

        if (message.invocationId === undefined) {
            throw new Error("Invalid payload for Completion message.");
        }
    }
}
