// Rough polyfill of https://developer.mozilla.org/en-US/docs/Web/API/AbortController
// We don't actually ever use the API being polyfilled, we always use the polyfill because
// it's a very new API right now.

export class AbortController implements AbortSignal {
    private isAborted: boolean = false;
    public onabort: () => void;

    abort() {
        if (!this.isAborted) {
            this.isAborted = true;
            if (this.onabort) {
                this.onabort();
            }
        }
    }

    get signal(): AbortSignal {
        return this;
    }

    get aborted(): boolean {
        return this.isAborted;
    }
}

export interface AbortSignal {
    aborted: boolean;
    onabort: () => void;
}
