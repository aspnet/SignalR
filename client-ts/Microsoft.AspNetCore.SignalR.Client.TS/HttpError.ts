export class HttpError extends Error {
    statusCode: number;
    constructor(errorMessage: string, errorStatusCode: number) {
        super(errorMessage);
        this.statusCode = errorStatusCode;
    }
}