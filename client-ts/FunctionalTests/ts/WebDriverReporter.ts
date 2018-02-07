import { getParameterByName } from "./Utils";

class WebDriverReporter implements jasmine.CustomReporter {
    private element: HTMLUListElement;
    private spec_counter: number = 1; // TAP number start at 1
    private record_counter: number = 0;

    constructor(private document: Document, show: boolean = false) {
        // We write to the DOM because it's the most compatible way for WebDriver to read.
        // For example, Chrome supports scraping console.log from WebDriver which would be ideal, but Firefox does not :(

        // Create an element for the output
        this.element = document.createElement("ul");
        this.element.setAttribute("id", "__tap_list");

        if (!show) {
            this.element.setAttribute("style", "display: none");
        }

        document.body.appendChild(this.element);
    }

    jasmineStarted(suiteInfo: jasmine.SuiteInfo): void {
        this.taplog(`1..${suiteInfo.totalSpecsDefined}`);
    }

    suiteStarted(result: jasmine.CustomReporterResult): void {
    }

    specStarted(result: jasmine.CustomReporterResult): void {
    }

    specDone(result: jasmine.CustomReporterResult): void {
        if (result.status === "failed") {
            this.taplog(`not ok ${this.spec_counter} ${result.fullName}`);

            // Include YAML block with failed expectations
            this.taplog(' ---');
            this.taplog(` message: ${result.failedExpectations.map(e => e.message).join(";")}`);
            this.taplog(' ...');
        }
        else {
            this.taplog(`ok ${this.spec_counter} ${result.fullName}`);
        }

        this.spec_counter += 1;
    }

    suiteDone(result: jasmine.CustomReporterResult): void {
    }

    jasmineDone(runDetails: jasmine.RunDetails): void {
        this.element.setAttribute("data-done", "1");
    }

    private taplog(msg: string) {
        for (let line of msg.split(/\r|\n|\r\n/)) {
            let li = this.document.createElement("li");
            li.setAttribute("style", "font-family: monospace; white-space: pre");
            li.setAttribute("id", `__tap_item_${this.record_counter}`);
            this.record_counter += 1;

            li.innerHTML = line;
            this.element.appendChild(li);
        }
    }
}

jasmine.getEnv().addReporter(new WebDriverReporter(window.document, getParameterByName("displayTap") === "true"));