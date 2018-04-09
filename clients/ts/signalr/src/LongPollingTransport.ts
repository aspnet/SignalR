// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

import { AbortController } from "./AbortController";
import { DataReceived, TransportClosed } from "./Common";
import { HttpError, TimeoutError } from "./Errors";
import { HttpClient, HttpRequest } from "./HttpClient";
import { IConnection } from "./IConnection";
import { ILogger, LogLevel } from "./ILogger";
import { ITransport, TransferFormat } from "./ITransport";
import { Arg, getDataDetail, sendMessage } from "./Utils";

export class LongPollingTransport implements ITransport {
    private readonly httpClient: HttpClient;
    private readonly accessTokenFactory: () => string;
    private readonly logger: ILogger;
    private readonly logMessageContent: boolean;

    private url: string;
    private pollXhr: XMLHttpRequest;
    private pollAbort: AbortController;

    constructor(httpClient: HttpClient, accessTokenFactory: () => string, logger: ILogger, logMessageContent: boolean) {
        this.httpClient = httpClient;
        this.accessTokenFactory = accessTokenFactory || (() => null);
        this.logger = logger;
        this.pollAbort = new AbortController();
        this.logMessageContent = logMessageContent;
    }

    public connect(url: string, transferFormat: TransferFormat, connection: IConnection): Promise<void> {
        Arg.isRequired(url, "url");
        Arg.isRequired(transferFormat, "transferFormat");
        Arg.isIn(transferFormat, TransferFormat, "transferFormat");
        Arg.isRequired(connection, "connection");

        this.url = url;

        this.logger.log(LogLevel.Trace, "(LongPolling transport) Connecting");

        // Set a flag indicating we have inherent keep-alive in this transport.
        connection.features.inherentKeepAlive = true;

        if (transferFormat === TransferFormat.Binary && (typeof new XMLHttpRequest().responseType !== "string")) {
            // This will work if we fix: https://github.com/aspnet/SignalR/issues/742
            throw new Error("Binary protocols over XmlHttpRequest not implementing advanced features are not supported.");
        }

        this.poll(this.url, transferFormat);
        return Promise.resolve();
    }

    private async poll(url: string, transferFormat: TransferFormat): Promise<void> {
        const pollOptions: HttpRequest = {
            abortSignal: this.pollAbort.signal,
            headers: {},
            timeout: 90000,
        };

        if (transferFormat === TransferFormat.Binary) {
            pollOptions.responseType = "arraybuffer";
        }

        const token = this.accessTokenFactory();
        if (token) {
            // tslint:disable-next-line:no-string-literal
            pollOptions.headers["Authorization"] = `Bearer ${token}`;
        }

        while (!this.pollAbort.signal.aborted) {
            try {
                const pollUrl = `${url}&_=${Date.now()}`;
                this.logger.log(LogLevel.Trace, `(LongPolling transport) polling: ${pollUrl}`);
                const response = await this.httpClient.get(pollUrl, pollOptions);
                if (response.statusCode === 204) {
                    this.logger.log(LogLevel.Information, "(LongPolling transport) Poll terminated by server");

                    // Poll terminated by server
                    if (this.onclose) {
                        this.onclose();
                    }
                    this.pollAbort.abort();
                } else if (response.statusCode !== 200) {
                    this.logger.log(LogLevel.Error, `(LongPolling transport) Unexpected response code: ${response.statusCode}`);

                    // Unexpected status code
                    if (this.onclose) {
                        this.onclose(new HttpError(response.statusText, response.statusCode));
                    }
                    this.pollAbort.abort();
                } else {
                    // Process the response
                    if (response.content) {
                        this.logger.log(LogLevel.Trace, `(LongPolling transport) data received. ${getDataDetail(response.content, this.logMessageContent)}`);
                        if (this.onreceive) {
                            this.onreceive(response.content);
                        }
                    } else {
                        // This is another way timeout manifest.
                        this.logger.log(LogLevel.Trace, "(LongPolling transport) Poll timed out, reissuing.");
                    }
                }
            } catch (e) {
                if (e instanceof TimeoutError) {
                    // Ignore timeouts and reissue the poll.
                    this.logger.log(LogLevel.Trace, "(LongPolling transport) Poll timed out, reissuing.");
                } else {
                    // Close the connection with the error as the result.
                    if (this.onclose) {
                        this.onclose(e);
                    }
                    this.pollAbort.abort();
                }
            }
        }
    }

    public async send(data: any): Promise<void> {
        return sendMessage(this.logger, "LongPolling", this.httpClient, this.url, this.accessTokenFactory, data, this.logMessageContent);
    }

    public stop(): Promise<void> {
        this.pollAbort.abort();
        return Promise.resolve();
    }

    public onreceive: DataReceived;
    public onclose: TransportClosed;
}
