interface EventSourceOptions {
    withcredentials: boolean;
}

declare class EventSource extends EventTarget {
    public readonly CLOSED: number;
    public readonly CONNECTING: number;
    public readonly OPEN: number;

    public readonly readyState: number;
    public readonly url: string;


    public onerror: (this: this, ev: ErrorEvent) => any;
    public onmessage: (this: this, ev: MessageEvent) => any;
    public onopen: (this: this, ev: Event) => any;


    constructor(url: string);
    constructor(url: string, configuration: EventSourceOptions);
    public close(): void;

    public addEventListener(type: 'error', listener: (this: this, ev: ErrorEvent) => any, useCapture?: boolean): void;
    public addEventListener(type: 'message', listener: (this: this, ev: MessageEvent) => any, useCapture?: boolean): void;
    public addEventListener(type: 'open', listener: (this: this, ev: Event) => any, useCapture?: boolean): void;
    public addEventListener(type: string, listener: EventListenerOrEventListenerObject, useCapture?: boolean): void;
}
