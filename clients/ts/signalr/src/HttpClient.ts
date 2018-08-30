// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

import * as HTTP from "http";
import { URL } from "url";
import { AbortSignal } from "./AbortController";
import { AbortError, HttpError, TimeoutError } from "./Errors";
import { ILogger, LogLevel } from "./ILogger";

/** Represents an HTTP request. */
export interface HttpRequest {
    /** The HTTP method to use for the request. */
    method?: string;

    /** The URL for the request. */
    url?: string;

    /** The body content for the request. May be a string or an ArrayBuffer (for binary data). */
    content?: string | ArrayBuffer;

    /** An object describing headers to apply to the request. */
    headers?: { [key: string]: string };

    /** The XMLHttpRequestResponseType to apply to the request. */
    responseType?: XMLHttpRequestResponseType;

    /** An AbortSignal that can be monitored for cancellation. */
    abortSignal?: AbortSignal;

    /** The time to wait for the request to complete before throwing a TimeoutError. Measured in milliseconds. */
    timeout?: number;
}

/** Represents an HTTP response. */
export class HttpResponse {
    /** Constructs a new instance of {@link @aspnet/signalr.HttpResponse} with the specified status code.
     *
     * @param {number} statusCode The status code of the response.
     */
    constructor(statusCode: number);

    /** Constructs a new instance of {@link @aspnet/signalr.HttpResponse} with the specified status code and message.
     *
     * @param {number} statusCode The status code of the response.
     * @param {string} statusText The status message of the response.
     */
    constructor(statusCode: number, statusText: string);

    /** Constructs a new instance of {@link @aspnet/signalr.HttpResponse} with the specified status code, message and string content.
     *
     * @param {number} statusCode The status code of the response.
     * @param {string} statusText The status message of the response.
     * @param {string} content The content of the response.
     */
    constructor(statusCode: number, statusText: string, content: string);

    /** Constructs a new instance of {@link @aspnet/signalr.HttpResponse} with the specified status code, message and binary content.
     *
     * @param {number} statusCode The status code of the response.
     * @param {string} statusText The status message of the response.
     * @param {ArrayBuffer} content The content of the response.
     */
    constructor(statusCode: number, statusText: string, content: ArrayBuffer);
    constructor(
        public readonly statusCode: number,
        public readonly statusText?: string,
        public readonly content?: string | ArrayBuffer) {
    }
}

/** Abstraction over an HTTP client.
 *
 * This class provides an abstraction over an HTTP client so that a different implementation can be provided on different platforms.
 */
export abstract class HttpClient {
    /** Issues an HTTP GET request to the specified URL, returning a Promise that resolves with an {@link @aspnet/signalr.HttpResponse} representing the result.
     *
     * @param {string} url The URL for the request.
     * @returns {Promise<HttpResponse>} A Promise that resolves with an {@link @aspnet/signalr.HttpResponse} describing the response, or rejects with an Error indicating a failure.
     */
    public get(url: string): Promise<HttpResponse>;

    /** Issues an HTTP GET request to the specified URL, returning a Promise that resolves with an {@link @aspnet/signalr.HttpResponse} representing the result.
     *
     * @param {string} url The URL for the request.
     * @param {HttpRequest} options Additional options to configure the request. The 'url' field in this object will be overridden by the url parameter.
     * @returns {Promise<HttpResponse>} A Promise that resolves with an {@link @aspnet/signalr.HttpResponse} describing the response, or rejects with an Error indicating a failure.
     */
    public get(url: string, options: HttpRequest): Promise<HttpResponse>;
    public get(url: string, options?: HttpRequest): Promise<HttpResponse> {
        return this.send({
            ...options,
            method: "GET",
            url,
        });
    }

    /** Issues an HTTP POST request to the specified URL, returning a Promise that resolves with an {@link @aspnet/signalr.HttpResponse} representing the result.
     *
     * @param {string} url The URL for the request.
     * @returns {Promise<HttpResponse>} A Promise that resolves with an {@link @aspnet/signalr.HttpResponse} describing the response, or rejects with an Error indicating a failure.
     */
    public post(url: string): Promise<HttpResponse>;

    /** Issues an HTTP POST request to the specified URL, returning a Promise that resolves with an {@link @aspnet/signalr.HttpResponse} representing the result.
     *
     * @param {string} url The URL for the request.
     * @param {HttpRequest} options Additional options to configure the request. The 'url' field in this object will be overridden by the url parameter.
     * @returns {Promise<HttpResponse>} A Promise that resolves with an {@link @aspnet/signalr.HttpResponse} describing the response, or rejects with an Error indicating a failure.
     */
    public post(url: string, options: HttpRequest): Promise<HttpResponse>;
    public post(url: string, options?: HttpRequest): Promise<HttpResponse> {
        return this.send({
            ...options,
            method: "POST",
            url,
        });
    }

    /** Issues an HTTP DELETE request to the specified URL, returning a Promise that resolves with an {@link @aspnet/signalr.HttpResponse} representing the result.
     *
     * @param {string} url The URL for the request.
     * @returns {Promise<HttpResponse>} A Promise that resolves with an {@link @aspnet/signalr.HttpResponse} describing the response, or rejects with an Error indicating a failure.
     */
    public delete(url: string): Promise<HttpResponse>;

    /** Issues an HTTP DELETE request to the specified URL, returning a Promise that resolves with an {@link @aspnet/signalr.HttpResponse} representing the result.
     *
     * @param {string} url The URL for the request.
     * @param {HttpRequest} options Additional options to configure the request. The 'url' field in this object will be overridden by the url parameter.
     * @returns {Promise<HttpResponse>} A Promise that resolves with an {@link @aspnet/signalr.HttpResponse} describing the response, or rejects with an Error indicating a failure.
     */
    public delete(url: string, options: HttpRequest): Promise<HttpResponse>;
    public delete(url: string, options?: HttpRequest): Promise<HttpResponse> {
        return this.send({
            ...options,
            method: "DELETE",
            url,
        });
    }

    /** Issues an HTTP request to the specified URL, returning a {@link Promise} that resolves with an {@link @aspnet/signalr.HttpResponse} representing the result.
     *
     * @param {HttpRequest} request An {@link @aspnet/signalr.HttpRequest} describing the request to send.
     * @returns {Promise<HttpResponse>} A Promise that resolves with an HttpResponse describing the response, or rejects with an Error indicating a failure.
     */
    public abstract send(request: HttpRequest): Promise<HttpResponse>;
}

/** Default implementation of {@link @aspnet/signalr.HttpClient}. */
export class DefaultHttpClient extends HttpClient {
    private readonly logger: ILogger;
    private readonly sendFunction: (request: HttpRequest) => Promise<HttpResponse>;

    /** Creates a new instance of the {@link @aspnet/signalr.DefaultHttpClient}, using the provided {@link @aspnet/signalr.ILogger} to log messages. */
    public constructor(logger: ILogger) {
        super();
        this.logger = logger;

        if (typeof XMLHttpRequest !== "undefined") {
            this.sendFunction = this.sendXhr;
        } else {
            this.sendFunction = this.sendNode;
        }
    }

    /** @inheritDoc */
    public send(request: HttpRequest): Promise<HttpResponse> {
        // Check that abort was not signaled before calling send
        if (request.abortSignal && request.abortSignal.aborted) {
            return Promise.reject(new AbortError());
        }

        if (!request.method) {
            return Promise.reject(new Error("No method defined."));
        }
        if (!request.url) {
            return Promise.reject(new Error("No url defined."));
        }

        return this.sendFunction(request);
    }

    private sendXhr(request: HttpRequest): Promise<HttpResponse> {
        return new Promise<HttpResponse>((resolve, reject) => {
            const xhr = new XMLHttpRequest();

            xhr.open(request.method!, request.url!, true);
            xhr.withCredentials = true;
            xhr.setRequestHeader("X-Requested-With", "XMLHttpRequest");
            // Explicitly setting the Content-Type header for React Native on Android platform.
            xhr.setRequestHeader("Content-Type", "text/plain;charset=UTF-8");

            const headers = request.headers;
            if (headers) {
                Object.keys(headers)
                    .forEach((header) => {
                        xhr.setRequestHeader(header, headers[header]);
                    });
            }

            if (request.responseType) {
                xhr.responseType = request.responseType;
            }

            if (request.abortSignal) {
                request.abortSignal.onabort = () => {
                    xhr.abort();
                    reject(new AbortError());
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
                this.logger.log(LogLevel.Warning, `Error from HTTP request. ${xhr.status}: ${xhr.statusText}`);
                reject(new HttpError(xhr.statusText, xhr.status));
            };

            xhr.ontimeout = () => {
                this.logger.log(LogLevel.Warning, `Timeout from HTTP request.`);
                reject(new TimeoutError());
            };

            xhr.send(request.content || "");
        });
    }

    private sendNode(request: HttpRequest): Promise<HttpResponse> {
        return new Promise<HttpResponse>((resolve, reject) => {
            const url = new URL(request.url!);
            const options: HTTP.RequestOptions = {
                headers: {
                    // Tell auth middleware to 401 instead of redirecting
                    "X-Requested-With": "XMLHttpRequest",
                    ...request.headers,
                },
                hostname: url.hostname,
                method: request.method,
                // /abc/xyz + ?id=12ssa_30
                path: url.pathname + url.search,
                port: url.port,
            };

            const data: Buffer[] = [];

            const req = HTTP.request(options, (res: HTTP.IncomingMessage) => {

                let dataLength = 0;
                res.on("data", (chunk: any) => {
                    data.push(chunk);
                    // Buffer.concat will be slightly faster if we keep track of the length
                    dataLength += chunk.length;
                });

                res.on("end", () => {
                    if (request.abortSignal) {
                        request.abortSignal.onabort = null;
                    }

                    if (res.statusCode && res.statusCode >= 200 && res.statusCode < 300) {
                        let resp: string | ArrayBuffer;
                        if (request.responseType === "arraybuffer") {
                            const buf = Buffer.concat(data, dataLength);
                            resolve(new HttpResponse(res.statusCode, res.statusMessage || "", buf));
                        } else {
                            resp = Buffer.concat(data, dataLength).toString();
                            resolve(new HttpResponse(res.statusCode, res.statusMessage || "", resp));
                        }
                    } else {
                        reject(new HttpError(res.statusMessage || "", res.statusCode || 0));
                    }
                });
            });

            if (request.abortSignal) {
                request.abortSignal.onabort = () => {
                    req.abort();
                    reject(new AbortError());
                };
            }

            if (request.timeout) {
                req.setTimeout(request.timeout, () => {
                    this.logger.log(LogLevel.Warning, `Timeout from HTTP request.`);
                    reject(new TimeoutError());
                });
            }

            req.on("error", (e) => {
                this.logger.log(LogLevel.Warning, `Error from HTTP request. ${e}`);
                reject(e);
            });

            if (request.content instanceof ArrayBuffer) {
                req.write(Buffer.from(request.content));
            } else {
                req.write(request.content || "");
            }
            req.end();
        });
    }
}
