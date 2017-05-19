import { IConnection } from "../Microsoft.AspNetCore.SignalR.Client.TS/IConnection"
import { HubConnection } from "../Microsoft.AspNetCore.SignalR.Client.TS/HubConnection"
import { DataReceived, ConnectionClosed } from "../Microsoft.AspNetCore.SignalR.Client.TS/Common"
import { TransportType, ITransport } from "../Microsoft.AspNetCore.SignalR.Client.TS/Transports"

import { asyncit as it, captureException } from './JasmineUtils';

describe("HubConnection", () => {
    it("completes pending invocations when stopped", async () => {
        let connection = new TestConnection();

        let hubConnection = new HubConnection(connection);
        var invokePromise = hubConnection.invoke("testMethod");
        hubConnection.stop();

        let ex = await captureException(async () => await invokePromise);
        expect(ex.message).toBe("Invocation cancelled due to connection being closed.");
    });

    it("completes pending invocations when connection is lost", async () => {
        let connection = new TestConnection();

        let hubConnection = new HubConnection(connection);
        var invokePromise = hubConnection.invoke("testMethod");
        // Typically this would be called by the transport
        connection.onClosed(new Error("Connection lost"));

        let ex = await captureException(async () => await invokePromise);
        expect(ex.message).toBe("Connection lost");
    });

    it("sends invocations as nonblocking", async () => {
        let connection = new TestConnection();

        let hubConnection = new HubConnection(connection);
        let invokePromise = hubConnection.invoke("testMethod");

        expect(connection.sentData.length).toBe(1);
        expect(JSON.parse(connection.sentData[0]).nonblocking).toBe(false);

        // will clean pending promises
        connection.onClosed();

        let ex = await captureException(async () => await invokePromise);
        // Don't care about the exception
    });

    it("rejects streaming responses made using 'invoke'", async () => {
        let connection = new TestConnection();

        let hubConnection = new HubConnection(connection);
        let invokePromise = hubConnection.invoke("testMethod");

        connection.onDataReceived("{ \"type\": 2, \"invocationId\": \"0\", \"result\": null }");
        connection.onClosed();

        let ex = await captureException(async () => await invokePromise);
        expect(ex.message).toBe("Streaming methods must be invoked using HubConnection.stream");
    });
});

class TestConnection implements IConnection {
    start(transportType: TransportType | ITransport): Promise<void> {
        return Promise.resolve();
    };

    send(data: any): Promise<void> {
        if (this.sentData) {
            this.sentData.push(data);
        }
        else {
            this.sentData = [data];
        }
        return Promise.resolve();
    };

    stop(): void {
        if (this.onClosed) {
            this.onClosed();
        }
    };

    onDataReceived: DataReceived;
    onClosed: ConnectionClosed;
    sentData: [any];
};
