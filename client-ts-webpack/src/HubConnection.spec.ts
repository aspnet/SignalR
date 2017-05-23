import { IConnection } from './IConnection';
import { HubConnection } from './HubConnection';
import { TransportType, ITransport } from './Transports';

describe('HubConnection', () => {
    it('completes pending invocations when stopped', async done => {
        let connection: IConnection = {
            start(_transportType: TransportType | ITransport): Promise<void> {
                return Promise.resolve();
            },

            send(_data: any): Promise<void> {
                return Promise.resolve();
            },

            stop(): void {
                if (this.onClosed) {
                    this.onClosed();
                }
            },

            onDataReceived: (_data: any) => {
                // Empty block
             },
            onClosed: (_e?: Error) => {
                // Empty block
             }
        };

        let hubConnection = new HubConnection(connection);
        let invokePromise = hubConnection.invoke('testMethod');
        hubConnection.stop();

        try {
            await invokePromise;
            fail();
        }
        catch (e) {
            expect(e.message).toBe('Invocation cancelled due to connection being closed.');
        }
        done();
    });

    it('completes pending invocations when connection is lost', async done => {
        let connection: IConnection = {
            start(_transportType: TransportType | ITransport): Promise<void> {
                return Promise.resolve();
            },

            send(_data: any): Promise<void> {
                return Promise.resolve();
            },

            stop(): void {
                if (this.onClosed) {
                    this.onClosed();
                }
            },

            onDataReceived: (_data: any) => {
                // Empty block
             },
            onClosed: (_e?: Error) => {
                // Empty block
             }
        };

        let hubConnection = new HubConnection(connection);
        let invokePromise = hubConnection.invoke('testMethod');
        // Typically this would be called by the transport
        connection.onClosed(new Error('Connection lost'));

        try {
            await invokePromise;
            fail();
        }
        catch (e) {
            expect(e.message).toBe('Connection lost');
        }
        done();
    });

    it('sends invocations as nonblocking', async done => {
        let dataSent: string = '';
        let connection: IConnection = {
            start(_transportType: TransportType): Promise<void> {
                return Promise.resolve();
            },

            send(data: any): Promise<void> {
                dataSent = data;
                return Promise.resolve();
            },

            stop(): void {
                if (this.onClosed) {
                    this.onClosed();
                }
            },

            onDataReceived: (_data: any) => {
                // Empty block
             },
            onClosed: (_e?: Error) => {
                // Empty block
             }
        };

        let hubConnection = new HubConnection(connection);
        let invokePromise = hubConnection.invoke('testMethod');

        expect(JSON.parse(dataSent).nonblocking).toBe(false);

        // will clean pending promises
        connection.onClosed();

        try {
            await invokePromise;
            fail(); // exception is expected because the call has not completed
        }
        catch (e) {
                // Empty block
             }
        done();
    });
    it('rejects streaming responses', async done => {
        let connection: IConnection = {
            start(_transportType: TransportType): Promise<void> {
                return Promise.resolve();
            },

            send(_data: any): Promise<void> {
                return Promise.resolve();
            },

            stop(): void {
                if (this.onClosed) {
                    this.onClosed();
                }
            },

            onDataReceived: (_data: any) => {
                // Empty block
            },
            onClosed: (_e?: Error) => {
                // Empty block
            }
        };

        let hubConnection = new HubConnection(connection);
        let invokePromise = hubConnection.invoke('testMethod');

        connection.onDataReceived('{ \"type\": 2, \"invocationId\": \"0\", \"result\": null }');
        connection.onClosed();

        try {
            await invokePromise;
            fail();
        }
        catch (e) {
            expect(e.message).toBe('Streaming is not supported.');
        }

        done();
    });
});
