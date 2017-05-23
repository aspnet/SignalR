import { IHttpClient } from './HttpClient';
import { Connection } from './Connection';
import { ISignalROptions } from './ISignalROptions';
import { ITransport } from './Transports';

describe('Connection', () => {

    it('starting connection fails if getting id fails', async (done) => {
        let options: ISignalROptions = {
            httpClient: <IHttpClient>{
                options(_url: string): Promise<string> {
                    return Promise.reject('error');
                },
                get(_url: string): Promise<string> {
                    return Promise.resolve('');
                }
            }
        } as ISignalROptions;

        let connection = new Connection('http://tempuri.org', undefined, options);

        try {
            await connection.start();
            fail();
            done();
        }
        catch (e) {
            expect(e).toBe('error');
            done();
        }
    });

    it('cannot start a running connection', async (done) => {
        let options: ISignalROptions = {
            httpClient: <IHttpClient>{
                options(_url: string): Promise<string> {
                    connection.start()
                        .then(() => {
                            fail();
                            done();
                        })
                        .catch((error: Error) => {
                            expect(error.message).toBe('Cannot start a connection that is not in the "Initial" state.');
                            done();
                        });

                    return Promise.reject('error');
                },
                get(_url: string): Promise<string> {
                    return Promise.resolve('');
                }
            }
        } as ISignalROptions;

        let connection = new Connection('http://tempuri.org', undefined, options);

        try {
            await connection.start();
        }
        catch (e) {
            // This exception is thrown after the actual verification is completed.
            // The connection is not setup to be running so just ignore the error.
        }
    });

    it('cannot start a stopped connection', async (done) => {
        let options: ISignalROptions = {
            httpClient: <IHttpClient>{
                options(_url: string): Promise<string> {
                    return Promise.reject('error');
                },
                get(_url: string): Promise<string> {
                    return Promise.resolve('');
                }
            }
        } as ISignalROptions;

        let connection = new Connection('http://tempuri.org', undefined, options);

        try {
            // start will fail and transition the connection to the Disconnected state
            await connection.start();
        }
        catch (e) {
            // The connection is not setup to be running so just ignore the error.
        }

        try {
            await connection.start();
            fail();
            done();
        }
        catch (e) {
            expect(e.message).toBe('Cannot start a connection that is not in the "Initial" state.');
            done();
        }
    });

    it('can stop a starting connection', async (done) => {
        let options: ISignalROptions = {
            httpClient: <IHttpClient>{
                options(_url: string): Promise<string> {
                    connection.stop();
                    return Promise.resolve('');
                },
                get(_url: string): Promise<string> {
                    connection.stop();
                    return Promise.resolve('');
                }
            }
        } as ISignalROptions;

        let connection = new Connection('http://tempuri.org', undefined, options);

        try {
            await connection.start();
            done();
        }
        catch (e) {
            fail();
            done();
        }
    });

    it('can stop a non-started connection', async (done) => {
        let connection = new Connection('http://tempuri.org');
        await connection.stop();
        done();
    });

    it('preserves users connection string', async done => {
        let options: ISignalROptions = {
            httpClient: <IHttpClient>{
                options(_url: string): Promise<string> {
                    return Promise.resolve('42');
                },
                get(_url: string): Promise<string> {
                    return Promise.resolve('');
                }
            }
        } as ISignalROptions;

        let connectQueryString: string = '';
        let fakeTransport: ITransport = {
            connect(_url: string, queryString: string): Promise<void> {
                connectQueryString = queryString;
                return Promise.reject('');
            },
            send(_data: any): Promise<void> {
                return Promise.reject('');
            },
            stop(): void { /* */ },
            onDataReceived: (_data: any) => { /* */ },
            onClosed: (_e?: Error) => { /* */ }
        };

        let connection = new Connection('http://tempuri.org', 'q=myData', options);

        try {
            await connection.start(fakeTransport);
            fail();
            done();
        }
        catch (e) { /* */ }

        expect(connectQueryString).toBe('q=myData&id=42');
        done();
    });
});
