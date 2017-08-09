﻿export const enum MessageType {
    Invocation = 1,
    Result,
    Completion
}

export interface HubMessage {
    readonly type: MessageType;
    readonly invocationId: string;
}

export interface InvocationMessage extends HubMessage {
    readonly target: string;
    readonly arguments: Array<any>;
    readonly nonblocking?: boolean;
}

export interface ResultMessage extends HubMessage {
    readonly item?: any;
}

export interface CompletionMessage extends HubMessage {
    readonly error?: string;
    readonly result?: any;
}

export interface NegotiationMessage {
    readonly protocol: string;
}

export const enum ProtocolType {
    Text = 1,
    Binary
}

export interface IHubProtocol {
    name(): string;
    type(): ProtocolType;
    parseMessages(input: any): HubMessage[];
    writeMessage(message: HubMessage): any;
}