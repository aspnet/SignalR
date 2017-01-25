import { ITransport, WebSocketTransport, ServerSentEventsTransport, LongPollingTransport } from "./Transports"
import { TransportType } from './TransportType';
import { IHttpClient, HttpClient } from "./HttpClient"

export class TransportFactory {

    constructor(private httpClient: IHttpClient) {
        this.httpClient = httpClient;
    }
    create(transportName: string): ITransport {
        if (transportName === TransportType.webSockets) {
            return new WebSocketTransport();
        }
        if (transportName === TransportType.serverSentEvents) {
            return new ServerSentEventsTransport(this.httpClient);
        }
        if (transportName === TransportType.longPolling) {
            return new LongPollingTransport(this.httpClient);
        }

        throw new Error("No valid transports requested.");
    }
}