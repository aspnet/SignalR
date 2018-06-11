// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

import { ILogger, LogLevel } from "../src/ILogger";
import { HttpTransportType } from "../src/ITransport";

export function eachTransport(action: (transport: HttpTransportType) => void) {
    const transportTypes = [
        HttpTransportType.WebSockets,
        HttpTransportType.ServerSentEvents,
        HttpTransportType.LongPolling ];
    transportTypes.forEach((t) => action(t));
}

export function eachEndpointUrl(action: (givenUrl: string, expectedUrl: string) => void) {
    const urls = [
        [ "http://tempuri.org/endpoint/?q=my/Data", "http://tempuri.org/endpoint/negotiate?q=my/Data" ],
        [ "http://tempuri.org/endpoint?q=my/Data", "http://tempuri.org/endpoint/negotiate?q=my/Data" ],
        [ "http://tempuri.org/endpoint", "http://tempuri.org/endpoint/negotiate" ],
        [ "http://tempuri.org/endpoint/", "http://tempuri.org/endpoint/negotiate" ],
    ];

    urls.forEach((t) => action(t[0], t[1]));
}

export class VerifyLogger implements ILogger {
    public unexpectedErrors: string[];
    private expectedErrors: string[];

    public constructor(...expectedErrors: string[]) {
        this.unexpectedErrors = new Array<string>();
        this.expectedErrors = expectedErrors;
    }

    public static async run(fn: (logger: VerifyLogger) => Promise<any>, ...expectedErrors: string[]): Promise<any> {
        const logger = new VerifyLogger(...expectedErrors);
        await fn(logger);
        expect(logger.unexpectedErrors.length).toBe(0);
    }

    public log(logLevel: LogLevel, message: string): void {
        if (logLevel >= LogLevel.Error) {
            if (this.expectedErrors.filter((error) => error === message).length === 0) {
                this.unexpectedErrors.push(message);
            }
        }
    }
}
