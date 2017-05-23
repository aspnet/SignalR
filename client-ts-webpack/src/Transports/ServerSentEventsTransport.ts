import * as Formatters from '../Formatters';
import { ITransport, IHttpClient, TransportClosed, DataReceived, SenderCallback } from '../index';

export class ServerSentEventsTransport implements ITransport {
    private eventSource: EventSource | null;
    private url: string;
    private queryString: string;
    private fullUrl: string;
    private httpClient: IHttpClient;
    private sender: SenderCallback;

    public onDataReceived: DataReceived;
    public onClosed: TransportClosed;

    public constructor(httpClient: IHttpClient, send: SenderCallback) {
        this.httpClient = httpClient;
        this.sender = send;
    }

    public connect(url: string, queryString: string): Promise<void> {
        if (typeof (EventSource) === 'undefined') {
            Promise.reject('EventSource not supported by the browser.');
        }

        this.queryString = queryString;
        this.url = url;
        this.fullUrl = url + (queryString ? '?' + queryString : '');

        return new Promise<void>((resolve, reject) => {
            let eventSource = new EventSource(this.fullUrl);

            try {
                eventSource.onmessage = (e: MessageEvent) => {
                    if (this.onDataReceived) {
                        // Parse the message
                        let message;
                        try {
                            message = Formatters.ServerSentEventsFormat.parse(e.data);
                        } catch (error) {
                            if (this.onClosed) {
                                this.onClosed(error);
                            }
                            return;
                        }

                        // TODO: pass the whole message object along
                        this.onDataReceived(message.content);
                    }
                };

                eventSource.onerror = (e: ErrorEvent) => {
                    reject();

                    // don't report an error if the transport did not start successfully
                    if (this.eventSource && this.onClosed) {
                        this.onClosed(new Error(e.message));
                    }
                };

                eventSource.onopen = () => {
                    this.eventSource = eventSource;
                    resolve();
                };
            }
            catch (e) {
                return Promise.reject(e);
            }
        });
    }

    public async send(data: any): Promise<void> {
        return this.sender(this.httpClient, this.fullUrl, data);
    }

    public stop(): void {
        if (this.eventSource) {
            this.eventSource.close();
            this.eventSource = null;
        }
    }
}
