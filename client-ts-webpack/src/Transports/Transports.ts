import { DataReceived, TransportClosed } from '../Common';
import { IHttpClient } from '../HttpClient';

export enum TransportType {
    WebSockets,
    ServerSentEvents,
    LongPolling
}

export interface ITransport {
    onDataReceived: DataReceived;
    onClosed: TransportClosed;
    connect(url: string, queryString: string): Promise<void>;
    send(data: any): Promise<void>;
    stop(): void;
}

export type SenderCallback = (httpClient: IHttpClient, url: string, data: any) => Promise<void>;

export async function send(httpClient: IHttpClient, url: string, data: any): Promise<void> {
    const headers = new Map<string, string>();
    headers.set('Content-Type', 'application/vnd.microsoft.aspnetcore.endpoint-messages.v1+text');

    let message = `T${data.length.toString()}:T:${data};`;
    await httpClient.post(url, message, headers);
}
