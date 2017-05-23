
import { DataReceived, TransportClosed, ITransport } from '../index';

export class WebSocketTransport implements ITransport {
    private webSocket: WebSocket | null;

    public onDataReceived: DataReceived;
    public onClosed: TransportClosed;

    public connect(url: string, queryString: string = ''): Promise<void> {
        return new Promise<void>((resolve, reject) => {
            url = url.replace(/^http/, 'ws');
            let connectUrl = url + (queryString ? '?' + queryString : '');

            let webSocket = new WebSocket(connectUrl);

            webSocket.onopen = (_event: Event) => {
                console.log(`WebSocket connected to ${connectUrl}`);
                this.webSocket = webSocket;
                resolve();
            };

            webSocket.onerror = (_event: Event) => {
                reject();
            };

            webSocket.onmessage = (message: MessageEvent) => {
                console.log(`(WebSockets transport) data received: ${message.data}`);
                if (this.onDataReceived) {
                    this.onDataReceived(message.data);
                }
            };

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
            };
        });
    }

    public send(data: any): Promise<void> {
        if (this.webSocket && this.webSocket.readyState === WebSocket.OPEN) {
            this.webSocket.send(data);
            return Promise.resolve();
        }

        return Promise.reject('WebSocket is not in the OPEN state');
    }

    public stop(): void {
        if (this.webSocket) {
            this.webSocket.close();
            this.webSocket = null;
        }
    }
}
