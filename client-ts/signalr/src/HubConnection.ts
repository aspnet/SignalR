// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

import { ConnectionClosed } from "./Common";
import { HttpConnection, IHttpConnectionOptions } from "./HttpConnection";
import { IConnection } from "./IConnection";
import { CancelInvocationMessage, CompletionMessage, HubMessage, IHubProtocol, InvocationMessage, MessageType, NegotiationMessage, StreamInvocationMessage, StreamItemMessage } from "./IHubProtocol";
import { ILogger, LogLevel } from "./ILogger";
import { JsonHubProtocol } from "./JsonHubProtocol";
import { ConsoleLogger, LoggerFactory, NullLogger } from "./Loggers";
import { Observable, Subject } from "./Observable";
import { TextMessageFormat } from "./TextMessageFormat";
import { TransferFormat, TransportType } from "./Transports";

export { JsonHubProtocol };

export interface IHubConnectionOptions extends IHttpConnectionOptions {
    protocol?: IHubProtocol;
    timeoutInMilliseconds?: number;
}

const DEFAULT_TIMEOUT_IN_MS: number = 30 * 1000;

export class HubConnection {
    private readonly connection: IConnection;
    private readonly logger: ILogger;
    private protocol: IHubProtocol;
    private callbacks: Map<string, (invocationEvent: StreamItemMessage | CompletionMessage, error?: Error) => void>;
    private methods: Map<string, Array<(...args: any[]) => void>>;
    private id: number;
    private closedCallbacks: ConnectionClosed[];
    private timeoutHandle: NodeJS.Timer;
    private timeoutInMilliseconds: number;

    constructor(url: string, options?: IHubConnectionOptions);
    constructor(connection: IConnection, options?: IHubConnectionOptions);
    constructor(urlOrConnection: string | IConnection, options: IHubConnectionOptions = {}) {
        options = options || {};

        this.timeoutInMilliseconds = options.timeoutInMilliseconds || DEFAULT_TIMEOUT_IN_MS;

        this.protocol = options.protocol || new JsonHubProtocol();

        if (typeof urlOrConnection === "string") {
            this.connection = new HttpConnection(urlOrConnection, this.protocol.transferFormat, options);
        } else {
            this.connection = urlOrConnection;
        }

        this.logger = LoggerFactory.createLogger(options.logger);

        this.connection.onreceive = (data: any) => this.processIncomingData(data);
        this.connection.onclose = (error?: Error) => this.connectionClosed(error);

        this.callbacks = new Map<string, (invocationEvent: HubMessage, error?: Error) => void>();
        this.methods = new Map<string, Array<(...args: any[]) => void>>();
        this.closedCallbacks = [];
        this.id = 0;
    }

    private processIncomingData(data: any) {
        if (this.timeoutHandle !== undefined) {
            clearTimeout(this.timeoutHandle);
        }

        // Parse the messages
        const messages = this.protocol.parseMessages(data);

        for (const message of messages) {
            switch (message.type) {
                case MessageType.Invocation:
                    this.invokeClientMethod(message);
                    break;
                case MessageType.StreamItem:
                case MessageType.Completion:
                    const callback = this.callbacks.get(message.invocationId);
                    if (callback != null) {
                        if (message.type === MessageType.Completion) {
                            this.callbacks.delete(message.invocationId);
                        }
                        callback(message);
                    }
                    break;
                case MessageType.Ping:
                    // Don't care about pings
                    break;
                default:
                    this.logger.log(LogLevel.Warning, "Invalid message type: " + data);
                    break;
            }
        }

        this.configureTimeout();
    }

    private configureTimeout() {
        if (!this.connection.features || !this.connection.features.inherentKeepAlive) {
            // Set the timeout timer
            this.timeoutHandle = setTimeout(() => this.serverTimeout(), this.timeoutInMilliseconds);
        }
    }

    private serverTimeout() {
        // The server hasn't talked to us in a while. It doesn't like us anymore ... :(
        // Terminate the connection
        this.connection.stop(new Error("Server timeout elapsed without receiving a message from the server."));
    }

    private invokeClientMethod(invocationMessage: InvocationMessage) {
        const methods = this.methods.get(invocationMessage.target.toLowerCase());
        if (methods) {
            methods.forEach((m) => m.apply(this, invocationMessage.arguments));
            if (invocationMessage.invocationId) {
                // This is not supported in v1. So we return an error to avoid blocking the server waiting for the response.
                const message = "Server requested a response, which is not supported in this version of the client.";
                this.logger.log(LogLevel.Error, message);
                this.connection.stop(new Error(message));
            }
        } else {
            this.logger.log(LogLevel.Warning, `No client method with the name '${invocationMessage.target}' found.`);
        }
    }

    private connectionClosed(error?: Error) {
        this.callbacks.forEach((callback) => {
            callback(undefined, error ? error : new Error("Invocation canceled due to connection being closed."));
        });
        this.callbacks.clear();

        this.closedCallbacks.forEach((c) => c.apply(this, [error]));

        this.cleanupTimeout();
    }

    public async start(): Promise<void> {
        await this.connection.start();

        await this.connection.send(
            TextMessageFormat.write(
                JSON.stringify({ protocol: this.protocol.name } as NegotiationMessage)));

        this.logger.log(LogLevel.Information, `Using HubProtocol '${this.protocol.name}'.`);

        this.configureTimeout();
    }

    public stop(): Promise<void> {
        this.cleanupTimeout();
        return this.connection.stop();
    }

    public stream<T>(methodName: string, ...args: any[]): Observable<T> {
        const invocationDescriptor = this.createStreamInvocation(methodName, args);

        const subject = new Subject<T>(() => {
            const cancelInvocation: CancelInvocationMessage = this.createCancelInvocation(invocationDescriptor.invocationId);
            const cancelMessage: any = this.protocol.writeMessage(cancelInvocation);

            this.callbacks.delete(invocationDescriptor.invocationId);

            return this.connection.send(cancelMessage);
        });

        this.callbacks.set(invocationDescriptor.invocationId, (invocationEvent: CompletionMessage | StreamItemMessage, error?: Error) => {
            if (error) {
                subject.error(error);
                return;
            }

            if (invocationEvent.type === MessageType.Completion) {
                if (invocationEvent.error) {
                    subject.error(new Error(invocationEvent.error));
                } else {
                    subject.complete();
                }
            } else {
                subject.next((invocationEvent.item) as T);
            }
        });

        const message = this.protocol.writeMessage(invocationDescriptor);

        this.connection.send(message)
            .catch((e) => {
                subject.error(e);
                this.callbacks.delete(invocationDescriptor.invocationId);
            });

        return subject;
    }

    public send(methodName: string, ...args: any[]): Promise<void> {
        const invocationDescriptor = this.createInvocation(methodName, args, true);

        const message = this.protocol.writeMessage(invocationDescriptor);

        return this.connection.send(message);
    }

    public invoke(methodName: string, ...args: any[]): Promise<any> {
        const invocationDescriptor = this.createInvocation(methodName, args, false);

        const p = new Promise<any>((resolve, reject) => {
            this.callbacks.set(invocationDescriptor.invocationId, (invocationEvent: StreamItemMessage | CompletionMessage, error?: Error) => {
                if (error) {
                    reject(error);
                    return;
                }
                if (invocationEvent.type === MessageType.Completion) {
                    const completionMessage = invocationEvent as CompletionMessage;
                    if (completionMessage.error) {
                        reject(new Error(completionMessage.error));
                    } else {
                        resolve(completionMessage.result);
                    }
                } else {
                    reject(new Error(`Unexpected message type: ${invocationEvent.type}`));
                }
            });

            const message = this.protocol.writeMessage(invocationDescriptor);

            this.connection.send(message)
                .catch((e) => {
                    reject(e);
                    this.callbacks.delete(invocationDescriptor.invocationId);
                });
        });

        return p;
    }

    public on(methodName: string, method: (...args: any[]) => void) {
        if (!methodName || !method) {
            return;
        }

        methodName = methodName.toLowerCase();
        if (!this.methods.has(methodName)) {
            this.methods.set(methodName, []);
        }

        this.methods.get(methodName).push(method);
    }

    public off(methodName: string, method: (...args: any[]) => void) {
        if (!methodName || !method) {
            return;
        }

        methodName = methodName.toLowerCase();
        const handlers = this.methods.get(methodName);
        if (!handlers) {
            return;
        }
        const removeIdx = handlers.indexOf(method);
        if (removeIdx !== -1) {
            handlers.splice(removeIdx, 1);
            if (handlers.length === 0) {
                this.methods.delete(methodName);
            }
        }
    }

    public onclose(callback: ConnectionClosed) {
        if (callback) {
            this.closedCallbacks.push(callback);
        }
    }

    private cleanupTimeout(): void {
        if (this.timeoutHandle) {
            clearTimeout(this.timeoutHandle);
        }
    }

    private createInvocation(methodName: string, args: any[], nonblocking: boolean): InvocationMessage {
        if (nonblocking) {
            return {
                arguments: args,
                target: methodName,
                type: MessageType.Invocation,
            };
        } else {
            const id = this.id;
            this.id++;

            return {
                arguments: args,
                invocationId: id.toString(),
                target: methodName,
                type: MessageType.Invocation,
            };
        }
    }

    private createStreamInvocation(methodName: string, args: any[]): StreamInvocationMessage {
        const id = this.id;
        this.id++;

        return {
            arguments: args,
            invocationId: id.toString(),
            target: methodName,
            type: MessageType.StreamInvocation,
        };
    }

    private createCancelInvocation(id: string): CancelInvocationMessage {
        return {
            invocationId: id,
            type: MessageType.CancelInvocation,
        };
    }
}
