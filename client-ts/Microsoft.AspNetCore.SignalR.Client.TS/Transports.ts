import { DataReceived, TransportClosed } from "./Common"
import { IHttpClient } from "./HttpClient"
import { HttpError } from "./HttpError"

export enum TransportType {
    WebSockets,
    ServerSentEvents,
    LongPolling
}

export const enum TransferMode {
    Text = 1,
    Binary
}

export interface ITransport {
    connect(url: string, requestedTransferMode: TransferMode): Promise<void>;
    send(data: any): Promise<void>;
    stop(): void;
    onDataReceived: DataReceived;
    onClosed: TransportClosed;
    transferMode(): TransferMode;
}

export class WebSocketTransport implements ITransport {
    private webSocket: WebSocket;
    private mode: TransferMode;

    connect(url: string, requestedTransferMode: TransferMode): Promise<void> {
        this.mode = requestedTransferMode;

        return new Promise<void>((resolve, reject) => {
            url = url.replace(/^http/, "ws");

            let webSocket = new WebSocket(url);
            if (requestedTransferMode == TransferMode.Binary) {
                webSocket.binaryType = "arraybuffer";
            }

            webSocket.onopen = (event: Event) => {
                console.log(`WebSocket connected to ${url}`);
                this.webSocket = webSocket;
                resolve();
            };

            webSocket.onerror = (event: Event) => {
                reject();
            };

            webSocket.onmessage = (message: MessageEvent) => {
                console.log(`(WebSockets transport) data received: ${message.data}`);
                if (this.onDataReceived) {
                    this.onDataReceived(message.data);
                }
            }

            webSocket.onclose = (event: CloseEvent) => {
                // webSocket will be null if the transport did not start successfully
                if (this.onClosed && this.webSocket) {
                    if (event.wasClean === false || event.code !== 1000) {
                        this.onClosed(new Error(`Websocket closed with status code: ${event.code} (${event.reason})`));
                    }
                    else {
                        this.onClosed();
                    }
                }
            }
        });
    }

    send(data: any): Promise<void> {
        if (this.webSocket && this.webSocket.readyState === WebSocket.OPEN) {
            this.webSocket.send(data);
            return Promise.resolve();
        }

        return Promise.reject("WebSocket is not in the OPEN state");
    }

    stop(): void {
        if (this.webSocket) {
            this.webSocket.close();
            this.webSocket = null;
        }
    }

    onDataReceived: DataReceived;
    onClosed: TransportClosed;

    transferMode(): TransferMode {
        return this.mode;
    }
}

export class ServerSentEventsTransport implements ITransport {
    private eventSource: EventSource;
    private url: string;
    private httpClient: IHttpClient;
    private mode: TransferMode;

    constructor(httpClient: IHttpClient) {
        this.httpClient = httpClient;
    }

    connect(url: string, requestedTransferMode: TransferMode): Promise<void> {
        if (typeof (EventSource) === "undefined") {
            Promise.reject("EventSource not supported by the browser.");
        }
        this.url = url;
        // SSE is a text protocol
        this.mode = TransferMode.Text;

        return new Promise<void>((resolve, reject) => {
            let eventSource = new EventSource(this.url);

            try {
                eventSource.onmessage = (e: MessageEvent) => {
                    if (this.onDataReceived) {
                        try {
                            console.log(`(SSE transport) data received: ${e.data}`);
                            this.onDataReceived(e.data);
                        } catch (error) {
                            if (this.onClosed) {
                                this.onClosed(error);
                            }
                            return;
                        }
                    }
                };

                eventSource.onerror = (e: ErrorEvent) => {
                    reject();

                    // don't report an error if the transport did not start successfully
                    if (this.eventSource && this.onClosed) {
                        this.onClosed(new Error(e.message || "Error occurred"));
                    }
                }

                eventSource.onopen = () => {
                    console.log(`SSE connected to ${this.url}`);
                    this.eventSource = eventSource;
                    resolve();
                }
            }
            catch (e) {
                return Promise.reject(e);
            }
        });
    }

    async send(data: any): Promise<void> {
        return send(this.httpClient, this.url, data);
    }

    stop(): void {
        if (this.eventSource) {
            this.eventSource.close();
            this.eventSource = null;
        }
    }

    onDataReceived: DataReceived;
    onClosed: TransportClosed;

    transferMode(): TransferMode {
        return this.mode;
    }
}

export class LongPollingTransport implements ITransport {
    private url: string;
    private httpClient: IHttpClient;
    private pollXhr: XMLHttpRequest;
    private shouldPoll: boolean;
    private mode: TransferMode;

    constructor(httpClient: IHttpClient) {
        this.httpClient = httpClient;
    }

    connect(url: string, requestedTransferMode: TransferMode): Promise<void> {
        this.url = url;
        this.mode = requestedTransferMode;
        this.shouldPoll = true;
        this.poll(this.url);
        return Promise.resolve();
    }

    private poll(url: string): void {
        if (!this.shouldPoll) {
            return;
        }

        let pollXhr = new XMLHttpRequest();
        if (this.mode == TransferMode.Binary) {
            pollXhr.responseType = "arraybuffer";
        }

        pollXhr.onload = () => {
            if (pollXhr.status == 200) {
                if (this.onDataReceived) {
                    try {
                        if (pollXhr.response) {
                            console.log(`(LongPolling transport) data received: ${pollXhr.response}`);
                            this.onDataReceived(pollXhr.response);
                        }
                        else {
                            console.log(`(LongPolling transport) timed out`);
                        }
                    } catch (error) {
                        if (this.onClosed) {
                            this.onClosed(error);
                        }
                        return;
                    }
                }
                this.poll(url);
            }
            else if (this.pollXhr.status == 204) {
                if (this.onClosed) {
                    this.onClosed();
                }
            }
            else {
                if (this.onClosed) {
                    this.onClosed(new HttpError(pollXhr.statusText, pollXhr.status));
                }
            }
        };

        pollXhr.onerror = () => {
            if (this.onClosed) {
                // network related error or denied cross domain request
                this.onClosed(new Error("Sending HTTP request failed."));
            }
        };

        pollXhr.ontimeout = () => {
            this.poll(url);
        }

        this.pollXhr = pollXhr;
        this.pollXhr.open("GET", url, true);
        // TODO: consider making timeout configurable
        this.pollXhr.timeout = 120000;
        this.pollXhr.send();
    }

    async send(data: any): Promise<void> {
        return send(this.httpClient, this.url, data);
    }

    stop(): void {
        this.shouldPoll = false;
        if (this.pollXhr) {
            this.pollXhr.abort();
            this.pollXhr = null;
        }
    }

    onDataReceived: DataReceived;
    onClosed: TransportClosed;

    transferMode(): TransferMode {
        return this.mode;
    }
}

const headers = new Map<string, string>();

async function send(httpClient: IHttpClient, url: string, data: any): Promise<void> {
    await httpClient.post(url, data, headers);
}