// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

import { HttpConnection, IHttpConnectionOptions } from "./HttpConnection";
import { HubConnection, JsonHubProtocol } from "./HubConnection";
import { IHubProtocol } from "./IHubProtocol";
import { ILogger, LogLevel } from "./ILogger";
import { NullLogger } from "./Loggers";
import { ConsoleLogger } from "./Utils";

export class HubConnectionBuilder {
    private protocol: any;
    private httpConnectionOptions: IHttpConnectionOptions;
    private url: string;
    private logger: ILogger;

    public configureLogging(logging: LogLevel | ILogger): HubConnectionBuilder {
        if (isLogger(logging)) {
            this.logger = logging;
        } else {
            this.logger = new ConsoleLogger(logging);
        }

        return this;
    }

    public withUrl(url: string): HubConnectionBuilder;
    public withUrl(url: string, options: IHttpConnectionOptions): HubConnectionBuilder;
    public withUrl(url: string, options?: IHttpConnectionOptions): HubConnectionBuilder {
        this.url = url;
        this.httpConnectionOptions = options;
        return this;
    }

    public withHubProtocol(protocol: IHubProtocol): HubConnectionBuilder {
        this.protocol = protocol;
        return this;
    }

    public build(): HubConnection {
        // If httpConnectionOptions has a logger, use it. Otherwise, override it with the one
        // provided to configureLogger
        const httpConnectionOptions = this.httpConnectionOptions || {};

        // If it's 'null', the user **explicitly** asked for null, don't mess with it.
        if (httpConnectionOptions.logger === undefined) {
            // If our logger is undefined or null, that's OK, the HttpConnection constructor will handle it.
            httpConnectionOptions.logger = this.logger;
        }

        // Now create the connection
        if (!this.url) {
            throw new Error("The 'HubConnectionBuilder.withUrl' method must be called before building the connection.");
        }
        const connection = new HttpConnection(this.url, httpConnectionOptions);

        return new HubConnection(
            connection,
            this.logger || NullLogger.instance,
            this.protocol || new JsonHubProtocol());
    }
}

function isLogger(logger: any): logger is ILogger {
    return logger.log !== undefined;
}
