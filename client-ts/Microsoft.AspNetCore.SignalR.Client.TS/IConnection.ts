import { DataReceived, ConnectionClosed } from "./Common"
import { TransportType, TransferMode, ITransport } from  "./Transports"

export interface IConnection {
    start(requestedTransferMode: TransferMode): Promise<void>;
    send(data: any): Promise<void>;
    stop(): void;
    transferMode(): TransferMode;

    onDataReceived: DataReceived;
    onClosed: ConnectionClosed;
}