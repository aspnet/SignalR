import { DataReceived, TransportClosed } from "./Common";
import { IConnection } from "./IConnection";

// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

export enum TransportType {
    WebSockets,
    ServerSentEvents,
    LongPolling,
}

export enum TransferFormat {
    Text = 1,
    Binary,
}

export interface ITransport {
    connect(url: string, transferFormat: TransferFormat): Promise<void>;
    send(data: any): Promise<void>;
    stop(): Promise<void>;
    onreceive: DataReceived;
    onclose: TransportClosed;
}
