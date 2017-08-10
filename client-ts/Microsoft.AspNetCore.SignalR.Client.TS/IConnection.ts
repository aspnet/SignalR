import { DataReceived, ConnectionClosed } from "./Common"
import { TransportType, TransferMode, ITransport } from  "./Transports"

export interface IConnection {
    start(requestedTransferMode: TransferMode): Promise<TransferMode>;
    send(data: any): Promise<void>;
    stop(): void;

    onDataReceived: DataReceived;
    onClosed: ConnectionClosed;
}