import { DataReceived, ConnectionClosed } from './Common';
import { TransportType, ITransport } from './Transports';

export interface IConnection {
    onDataReceived: DataReceived;
    onClosed: ConnectionClosed;

    start(transportType: TransportType | ITransport): Promise<void>;
    send(data: any): Promise<void>;
    stop(): void;
}
