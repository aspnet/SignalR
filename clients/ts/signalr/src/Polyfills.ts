// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

import { w3cwebsocket } from "websocket";

// Not exported from index

export interface EventSourceConstructor {
    new(url: string, eventSourceInitDict?: EventSourceInit): EventSource;
}

export interface WebSocketConstructor {
    new(url: string, protocols?: string | string[]): WebSocket;
    readonly CLOSED: number;
    readonly CLOSING: number;
    readonly CONNECTING: number;
    readonly OPEN: number;
}

export class WebSocketWrapper implements WebSocket {
    // tslint:disable-next-line:variable-name
    private _websocket: w3cwebsocket;

    constructor(url: string, protocols?: string | string[]) {
        this._websocket = new w3cwebsocket(url, protocols as any);
    }

    public get binaryType(): BinaryType {
        return (this._websocket as any).binaryType;
    }

    public set binaryType(type: BinaryType) {
        if (type === "arraybuffer") {
            (this._websocket as any).binaryType = type;
        }
        // "blob" not supported by w3cwebsocket
    }

    public get bufferedAmount(): number {
        return this._websocket.bufferedAmount;
    }

    public set bufferedAmount(num: number) {
        this._websocket.bufferedAmount = num;
    }

    public get extensions(): string {
        return (this._websocket as any).extensions.join(",");
    }

    // tslint:disable-next-line:variable-name
    public set extensions(_extensions: string) {
        // extensions isn't used
    }

    public get onclose(): ((this: WebSocket, ev: CloseEvent) => any) | null {
        return this._websocket.onclose;
    }
    public set onclose(close: ((this: WebSocket, ev: CloseEvent) => any) | null) {
        this._websocket.onclose = close as any;
    }

    public get onerror(): ((this: WebSocket, ev: Event) => any) | null {
        return this._websocket.onerror as any;
    }
    public set onerror(error: ((this: WebSocket, ev: Event) => any) | null) {
        this._websocket.onerror = error as any;
    }

    public get onmessage(): ((this: WebSocket, ev: MessageEvent) => any) | null {
        return this._websocket.onmessage;
    }
    public set onmessage(message: ((this: WebSocket, ev: MessageEvent) => any) | null) {
        this._websocket.onmessage = message as any;
    }

    public get onopen(): ((this: WebSocket, ev: Event) => any) | null {
        return this._websocket.onopen;
    }
    public set onopen(open: ((this: WebSocket, ev: Event) => any) | null) {
        this._websocket.onopen = open as any;
    }

    public get protocol(): string {
        return this._websocket.protocol || "";
    }
    public set protocol(protocol: string) {
        this._websocket.protocol = protocol;
    }

    public get readyState(): number {
        return this._websocket.readyState;
    }
    public set readyState(readyState: number) {
        this._websocket.readyState = readyState;
    }

    public get url(): string {
        return this._websocket.url;
    }
    public set url(url: string) {
        this._websocket.url = url;
    }

    public close(code?: number | undefined, reason?: string | undefined): void {
        this._websocket.close(code, reason);
    }

    public send(data: string | ArrayBuffer | Blob | ArrayBufferView): void {
        this._websocket.send(data);
    }

    public readonly CLOSED: number = w3cwebsocket.CLOSED;
    public static readonly CLOSED: number = w3cwebsocket.CLOSED;
    public readonly CLOSING: number = w3cwebsocket.CLOSING;
    public static readonly CLOSING: number = w3cwebsocket.CLOSING;
    public readonly CONNECTING: number = w3cwebsocket.CONNECTING;
    public static readonly CONNECTING: number = w3cwebsocket.CONNECTING;
    public readonly OPEN: number = w3cwebsocket.OPEN;
    public static readonly OPEN: number = w3cwebsocket.OPEN;

    public addEventListener<K extends "close" | "error" | "message" | "open">(type: K, listener: (this: WebSocket, ev: WebSocketEventMap[K]) => any, options?: boolean | AddEventListenerOptions | undefined): void;
    public addEventListener(type: string, listener: EventListenerOrEventListenerObject, options?: boolean | AddEventListenerOptions | undefined): void;
    // tslint:disable-next-line:variable-name
    public addEventListener(_type: any, _listener: any, _options?: any) {
        throw new Error("Method not implemented.");
    }
    public removeEventListener<K extends "close" | "error" | "message" | "open">(type: K, listener: (this: WebSocket, ev: WebSocketEventMap[K]) => any, options?: boolean | EventListenerOptions | undefined): void;
    public removeEventListener(type: string, listener: EventListenerOrEventListenerObject, options?: boolean | EventListenerOptions | undefined): void;
    // tslint:disable-next-line:variable-name
    public removeEventListener(_type: any, _listener: any, _options?: any) {
        throw new Error("Method not implemented.");
    }
    // tslint:disable-next-line:variable-name
    public dispatchEvent(_evt: Event): boolean {
        throw new Error("Method not implemented.");
    }
}
