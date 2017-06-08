import { DataReceived, TransportClosed, IHttpClient, ITransport, SenderCallback } from '../index';
import * as Formatters from '../Formatters';

export class LongPollingTransport implements ITransport {
    private url: string;
    private queryString: string;
    private fullUrl: string;
    private httpClient: IHttpClient;
    private pollXhr: XMLHttpRequest | null;
    private shouldPoll: boolean;
    private senderCallback: SenderCallback;

    public onDataReceived: DataReceived;
    public onClosed: TransportClosed;

    constructor(httpClient: IHttpClient, senderCallback: SenderCallback) {
        this.httpClient = httpClient;
        this.senderCallback = senderCallback;
    }

    public connect(url: string, queryString: string): Promise<void> {
        this.url = url;
        this.queryString = queryString;
        this.shouldPoll = true;
        this.fullUrl = url + (queryString ? '?' + queryString : '');
        this.poll(this.fullUrl);
        return Promise.resolve();
    }

    private poll(url: string): void {
        if (!this.shouldPoll) {
            return;
        }

        let pollXhr = new XMLHttpRequest();

        pollXhr.onload = () => {
            if (pollXhr.status === 200) {
                if (this.onDataReceived) {
                    // Parse the messages
                    let messages;
                    try {
                        messages = Formatters.TextMessageFormat.parse(pollXhr.response);
                    } catch (error) {
                        if (this.onClosed) {
                            this.onClosed(error);
                        }
                        return;
                    }

                    messages.forEach((message) => {
                        // TODO: pass the whole message object along
                        this.onDataReceived(message.content);
                    });
                }
                this.poll(url);
            }
            else if (pollXhr.status === 204) {
                if (this.onClosed) {
                    this.onClosed();
                }
            }
            else {
                if (this.onClosed) {
                    this.onClosed(new Error(`Status: ${pollXhr.status}, Message: ${pollXhr.responseText}`));
                }
            }
        };

        pollXhr.onerror = () => {
            if (this.onClosed) {
                // network related error or denied cross domain request
                this.onClosed(new Error('Sending HTTP request failed.'));
            }
        };

        pollXhr.ontimeout = () => {
            this.poll(url);
        };

        this.pollXhr = pollXhr;
        this.pollXhr.open('GET', url, true);
        // TODO: consider making timeout configurable
        this.pollXhr.timeout = 110000;
        this.pollXhr.send();
    }

    public async send(data: any): Promise<void> {
        return this.senderCallback(this.httpClient, this.fullUrl, data);
    }

    public stop(): void {
        this.shouldPoll = false;
        if (this.pollXhr) {
            this.pollXhr.abort();
            this.pollXhr = null;
        }
    }
}
