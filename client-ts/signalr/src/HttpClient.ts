// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

import { AbortSignal } from "./AbortController";
import { HttpError, TimeoutError } from "./Errors";

export interface HttpRequest {
    method?: string;
    url?: string;
    content?: string | ArrayBuffer;
    headers?: Map<string, string>;
    responseType?: XMLHttpRequestResponseType;
    abortSignal?: AbortSignal;
    timeout?: number;
}

export class HttpResponse {
    constructor(statusCode: number, statusText: string, content: string);
    constructor(statusCode: number, statusText: string, content: ArrayBuffer);
    constructor(
        public readonly statusCode: number,
        public readonly statusText: string,
        public readonly content: string | ArrayBuffer) {
    }
}

export abstract class HttpClient {
    public get(url: string): Promise<HttpResponse>;
    public get(url: string, options: HttpRequest): Promise<HttpResponse>;
    public get(url: string, options?: HttpRequest): Promise<HttpResponse> {
        return this.send({
            ...options,
            method: "GET",
            url,
        });
    }

    public post(url: string): Promise<HttpResponse>;
    public post(url: string, options: HttpRequest): Promise<HttpResponse>;
    public post(url: string, options?: HttpRequest): Promise<HttpResponse> {
        return this.send({
            ...options,
            method: "POST",
            url,
        });
    }

    public abstract send(request: HttpRequest): Promise<HttpResponse>;
}

export class DefaultHttpClient extends HttpClient {
    public send(request: HttpRequest): Promise<HttpResponse> {
        return new Promise<HttpResponse>((resolve, reject) => {
            const xhr = new XMLHttpRequest();

            xhr.open(request.method, request.url, true);
            xhr.setRequestHeader("X-Requested-With", "XMLHttpRequest");

            if (request.headers) {
                request.headers.forEach((value, header) => xhr.setRequestHeader(header, value));
            }

            if (request.responseType) {
                xhr.responseType = request.responseType;
            }

            if (request.abortSignal) {
                request.abortSignal.onabort = () => {
                    xhr.abort();
                };
            }

            if (request.timeout) {
                xhr.timeout = request.timeout;
            }

            xhr.onload = () => {
                if (request.abortSignal) {
                    request.abortSignal.onabort = null;
                }

                if (xhr.status >= 200 && xhr.status < 300) {
                    resolve(new HttpResponse(xhr.status, xhr.statusText, xhr.response || xhr.responseText));
                } else {
                    reject(new HttpError(xhr.statusText, xhr.status));
                }
            };

            xhr.onerror = () => {
                reject(new HttpError(xhr.statusText, xhr.status));
            };

            xhr.ontimeout = () => {
                reject(new TimeoutError());
            };

            xhr.send(request.content || "");
        });
    }
}
