import { IHttpClient } from "../Microsoft.AspNetCore.SignalR.Client.TS/HttpClient"
import { HttpConnection } from "../Microsoft.AspNetCore.SignalR.Client.TS/HttpConnection"
import { IHttpConnectionOptions } from "../Microsoft.AspNetCore.SignalR.Client.TS/IHttpConnectionOptions"
import { DataReceived, TransportClosed } from "../Microsoft.AspNetCore.SignalR.Client.TS/Common"
import { ITransport, TransportType, TransferMode } from "../Microsoft.AspNetCore.SignalR.Client.TS/Transports"
import { eachTransport } from "./Common";

describe("Connection", () => {

    it("starting connection fails if getting id fails", async (done) => {
        let options: IHttpConnectionOptions = {
            httpClient: <IHttpClient>{
                options(url: string): Promise<string> {
                    return Promise.reject("error");
                },
                get(url: string): Promise<string> {
                    return Promise.resolve("");
                }
            }
        } as IHttpConnectionOptions;

        let connection = new HttpConnection("http://tempuri.org", options);

        try {
            await connection.start(TransferMode.Text);
            fail();
            done();
        }
        catch (e) {
            expect(e).toBe("error");
            done();
        }
    });

    it("cannot start a running connection", async (done) => {
        let options: IHttpConnectionOptions = {
            httpClient: <IHttpClient>{
                options(url: string): Promise<string> {
                    connection.start(TransferMode.Text)
                        .then(() => {
                            fail();
                            done();
                        })
                        .catch((error: Error) => {
                            expect(error.message).toBe("Cannot start a connection that is not in the 'Initial' state.");
                            done();
                        });

                    return Promise.reject("error");
                },
                get(url: string): Promise<string> {
                    return Promise.resolve("");
                }
            }
        } as IHttpConnectionOptions;

        let connection = new HttpConnection("http://tempuri.org", options);

        try {
            await connection.start(TransferMode.Text);
        }
        catch (e) {
            // This exception is thrown after the actual verification is completed.
            // The connection is not setup to be running so just ignore the error.
        }
    });

    it("cannot start a stopped connection", async (done) => {
        let options: IHttpConnectionOptions = {
            httpClient: <IHttpClient>{
                options(url: string): Promise<string> {
                    return Promise.reject("error");
                },
                get(url: string): Promise<string> {
                    return Promise.resolve("");
                }
            }
        } as IHttpConnectionOptions;

        let connection = new HttpConnection("http://tempuri.org", options);

        try {
            // start will fail and transition the connection to the Disconnected state
            await connection.start(TransferMode.Text);
        }
        catch (e) {
            // The connection is not setup to be running so just ignore the error.
        }

        try {
            await connection.start(TransferMode.Text);
            fail();
            done();
        }
        catch (e) {
            expect(e.message).toBe("Cannot start a connection that is not in the 'Initial' state.");
            done();
        }
    });

    it("can stop a starting connection", async (done) => {
        let options: IHttpConnectionOptions = {
            httpClient: <IHttpClient>{
                options(url: string): Promise<string> {
                    connection.stop();
                    return Promise.resolve("{}");
                },
                get(url: string): Promise<string> {
                    connection.stop();
                    return Promise.resolve("");
                }
            }
        } as IHttpConnectionOptions;

        let connection = new HttpConnection("http://tempuri.org", options);

        try {
            await connection.start(TransferMode.Text);
            done();
        }
        catch (e) {
            fail();
            done();
        }
    });

    it("can stop a non-started connection", async (done) => {
        let connection = new HttpConnection("http://tempuri.org");
        await connection.stop();
        done();
    });

    it("preserves users connection string", async done => {
        let connectUrl: string;
        let fakeTransport: ITransport = {
            connect(url: string): Promise<void> {
                connectUrl = url;
                return Promise.reject("");
            },
            send(data: any): Promise<void> {
                return Promise.reject("");
            },
            stop(): void { },
            onDataReceived: undefined,
            onClosed: undefined,
            transferMode(): TransferMode {
                return TransferMode.Text;
            }
        }

        let options: IHttpConnectionOptions = {
            httpClient: <IHttpClient>{
                options(url: string): Promise<string> {
                    return Promise.resolve("{ \"connectionId\": \"42\" }");
                },
                get(url: string): Promise<string> {
                    return Promise.resolve("");
                }
            },
            transport: fakeTransport
        } as IHttpConnectionOptions;


        let connection = new HttpConnection("http://tempuri.org?q=myData", options);

        try {
            await connection.start(TransferMode.Text);
            fail();
            done();
        }
        catch (e) {
        }

        expect(connectUrl).toBe("http://tempuri.org?q=myData&id=42");
        done();
    });

    eachTransport((requestedTransport: TransportType) => {
        it(`cannot be started if requested ${TransportType[requestedTransport]} transport not available on server`, async done => {
            let options: IHttpConnectionOptions = {
                httpClient: <IHttpClient>{
                    options(url: string): Promise<string> {
                        return Promise.resolve("{ \"connectionId\": \"42\", \"availableTransports\": [] }");
                    },
                    get(url: string): Promise<string> {
                        return Promise.resolve("");
                    }
                },
                transport: requestedTransport
            } as IHttpConnectionOptions;

            let connection = new HttpConnection("http://tempuri.org", options);
            try {
                await connection.start(TransferMode.Text);
                fail();
                done();
            }
            catch (e) {
                expect(e.message).toBe("No available transports found.");
                done();
            }
        });
    });

    it("cannot be started if no transport available on server and no transport requested", async done => {
        let options: IHttpConnectionOptions = {
            httpClient: <IHttpClient>{
                options(url: string): Promise<string> {
                    return Promise.resolve("{ \"connectionId\": \"42\", \"availableTransports\": [] }");
                },
                get(url: string): Promise<string> {
                    return Promise.resolve("");
                }
            }
        } as IHttpConnectionOptions;

        let connection = new HttpConnection("http://tempuri.org", options);
        try {
            await connection.start(TransferMode.Text);
            fail();
            done();
        }
        catch (e) {
            expect(e.message).toBe("No available transports found.");
            done();
        }
    });

    it("transfer mode is null when the connection is not started", () => {
        expect(new HttpConnection("https://tempuri.org").transferMode()).toBeNull();
    });

    [
        [TransferMode.Text, TransferMode.Text],
        [TransferMode.Text, TransferMode.Binary],
        [TransferMode.Binary, TransferMode.Text],
        [TransferMode.Binary, TransferMode.Binary],
    ].forEach(([requestedTransferMode, transportTransferMode]) => {
        it(`connection returns ${transportTransferMode} transfer mode when ${requestedTransferMode} transfer mode is requested`, async () => {
            let fakeTransport = {
                // mode: TransferMode : TransferMode.Text
                connect(url: string, requestedTransferMode: TransferMode): Promise<void> { return Promise.resolve(); },
                send(data: any): Promise<void> { return Promise.resolve(); },
                stop(): void {},
                onDataReceived: null,
                onClosed: null,
                transferMode(): TransferMode { return this.mode; },
                mode: transportTransferMode
            } as ITransport;

            let options: IHttpConnectionOptions = {
                httpClient: <IHttpClient>{
                    options(url: string): Promise<string> {
                        return Promise.resolve("{ \"connectionId\": \"42\", \"availableTransports\": [] }");
                    },
                    get(url: string): Promise<string> {
                        return Promise.resolve("");
                    }
                },
                transport: fakeTransport
            } as IHttpConnectionOptions;

            let connection = new HttpConnection("https://tempuri.org", options);
            await connection.start(requestedTransferMode);
            expect(connection.transferMode()).toBe(transportTransferMode);
        });
    });
});
