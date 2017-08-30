// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

import { IHttpClient } from "./HttpClient"
import { TransportType, ITransport } from "./Transports"
import { ILogger } from "./ILogger";

export interface IHttpConnectionOptions {
    httpClient?: IHttpClient;
    transport?: TransportType | ITransport;
    logger?: ILogger;
}
