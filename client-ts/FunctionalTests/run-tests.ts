// console.log messages should be prefixed with "#" to ensure stdout continues to conform to TAP (Test Anything Protocol)
// https://testanything.org/tap-version-13-specification.html

import { ChildProcess, spawn, spawnSync } from "child_process";
import * as os from "os";
import * as path from "path";
import { Readable } from "stream";

import { Builder, By, Capabilities, logging, WebDriver, WebElement } from "selenium-webdriver";
import * as kill from "tree-kill";
import { argv } from "yargs";

const rootDir = __dirname;

const verbose = argv.v || argv.verbose || false;
const browser = argv.browser || "chrome";
const headless = argv.headless || argv.h || false;

console.log("TAP version 13");

function logverbose(message: any) {
    if (verbose) {
        console.log(message);
    }
}

function runCommand(command: string, args: string[]) {
    args = args || [];
    const result = spawnSync(command, args, {
        cwd: rootDir,
    });
    if (result.status !== 0) {
        console.error("Bail out!"); // Part of the TAP protocol
        console.error(`Command ${command} ${args.join(" ")} failed:`);
        console.error("stderr:");
        console.error(result.stderr);
        console.error("stdout:");
        console.error(result.stdout);
        cleanup(() => process.exit(result.status));
    }
}

const logExtractorRegex = /[^ ]+ [^ ]+ "(.*)"/;

function getMessage(logMessage: string): string {
    const r = logExtractorRegex.exec(logMessage);

    // Unescape \"
    if (r && r.length >= 2) {
        return r[1].replace(/\\"/g, "\"");
    } else {
        return logMessage;
    }
}

async function waitForElement(driver: WebDriver, id: string): Promise<WebElement> {
    while (true) {
        const elements = await driver.findElements(By.id(id));
        if (elements && elements.length > 0) {
            return elements[0];
        }
    }
}

async function isComplete(element: WebElement): Promise<boolean> {
    return (await element.getAttribute("data-done")) === "1";
}

async function getLogEntry(index: number, element: WebElement): Promise<WebElement> {
    const elements = await element.findElements(By.id(`__tap_item_${index}`));
    if (elements && elements.length > 0) {
        return elements[0];
    }
    return null;
}

async function getEntryContent(element: WebElement): Promise<string> {
    return await element.getAttribute("innerHTML");
}

async function flushEntries(index: number, element: WebElement): Promise<void> {
    let entry = await getLogEntry(index, element);
    while (entry) {
        index += 1;
        console.log(await getEntryContent(entry));
        entry = await getLogEntry(index, element);
    }
}

function applyCapabilities(builder: Builder) {
    if (browser === "chrome") {
        const caps = Capabilities.chrome();
        const args = [];

        if (headless) {
            console.log("# Using Headless Mode");
            args.push("--headless");
            if (process.platform === "win32") {
                args.push("--disable-gpu");
            }
        }

        caps.set("chromeOptions", {
            args,
        });
        builder.withCapabilities(caps);
    }
}

async function runTests(port: number, serverUrl: string): Promise<void> {
    const webDriverUrl = `http://localhost:${port}/wd/hub`;
    console.log(`# Using WebDriver at ${webDriverUrl}`);
    console.log(`# Launching ${browser} browser`);
    const logPrefs = new logging.Preferences();
    logPrefs.setLevel(logging.Type.BROWSER, logging.Level.INFO);

    const builder = new Builder()
        .usingServer(webDriverUrl)
        .setLoggingPrefs(logPrefs)
        .forBrowser(browser);

    applyCapabilities(builder);

    const driver = await builder.build();
    try {
        await driver.get(serverUrl);

        let index = 0;
        console.log("# Running tests");
        const element = await waitForElement(driver, "__tap_list");
        const success = true;
        while (!await isComplete(element)) {
            const entry = await getLogEntry(index, element);
            if (entry) {
                index += 1;
                console.log(await getEntryContent(entry));
            }
        }

        // Flush remaining entries
        await flushEntries(index, element);
        console.log("# End of tests");
    } catch (e) {
        console.error("Error: " + e.toString());
    } finally {
        await driver.quit();
    }
}

function waitForMatch(command: string, process: ChildProcess, regex: RegExp, onMatch: (RegExpMatchArray) => void) {
    let lastLine = "";

    async function onData(this: Readable, chunk: string | Buffer): Promise<void> {
        chunk = chunk.toString();

        // Process lines
        let lineEnd = chunk.indexOf(os.EOL);
        while (lineEnd >= 0) {
            const chunkLine = lastLine + chunk.substring(0, lineEnd);
            lastLine = "";

            chunk = chunk.substring(lineEnd + os.EOL.length);

            logverbose(`# ${command}: ${chunkLine}`);
            const results = regex.exec(chunkLine);
            if (results && results.length > 0) {
                onMatch(results);
                this.removeAllListeners("data");
                return;
            }
            lineEnd = chunk.indexOf(os.EOL);
        }
        lastLine = chunk.toString();
    }

    process.on("close", (code, signal) => {
        console.log(`# ${command} process exited with code: ${code}`);
        cleanup(() => global.process.exit(code));
    });

    process.stdout.on("data", onData.bind(process.stdout));
    process.stderr.on("data", onData.bind(process.stderr));
}

const webDriverManagerPath = path.resolve(__dirname, "node_modules", "webdriver-manager", "bin", "webdriver-manager");

// This script launches the functional test app and then uses Selenium WebDriver to run the tests and verify the results.
console.log("# Updating WebDrivers...");
runCommand(process.execPath, [webDriverManagerPath, "update"]);
console.log("# Updated WebDrivers");

let webDriver;
let dotnet;

function cleanupDotNet(cb: () => void) {
    if (dotnet && !dotnet.killed) {
        console.log(`# Killing dotnet process (PID: ${dotnet.pid})`);
        kill(dotnet.pid, "SIGTERM", () => {
            console.log("# Killed dotnet process");
            cb();
        });
    } else {
        cb();
    }
}

function cleanup(cb: () => void) {
    if (webDriver && !webDriver.killed) {
        console.log(`# Killing webdriver-manager process (PID: ${webDriver.pid})`);
        kill(webDriver.pid, "SIGTERM", () => {
            console.log("# Killed webdriver-manager process");
            cleanupDotNet(cb);
        });
    } else {
        cleanupDotNet(cb);
    }
}

console.log("# Launching WebDriver...");
webDriver = spawn(process.execPath, [webDriverManagerPath, "start"]);

const webDriverRegex = /\d+:\d+:\d+.\d+ INFO - Selenium Server is up and running on port (\d+)/;

// The message we're waiting for is written to stderr for some reason
waitForMatch("webdriver-server", webDriver, webDriverRegex, (results) => {
    console.log("# WebDriver Launched");
    webDriverLaunched(Number.parseInt(results[1]), () => {
        try {
            // Clean up is automatic
            cleanup(() => process.exit(0));
        } catch (e) {
            console.error(`Bail out! Error terminating WebDriver: ${e}`);
            cleanup(() => process.exit(1));
        }
    });
});

function webDriverLaunched(webDriverPort: number, cb: () => void) {
    console.log("# Launching Functional Test server...");
    dotnet = spawn("dotnet", [path.resolve(__dirname, "bin", "Debug", "netcoreapp2.1", "FunctionalTests.dll")], {
        cwd: rootDir,
    });

    const regex = /Now listening on: (http:\/\/localhost:([\d])+)/;
    waitForMatch("dotnet", dotnet, regex, async (results) => {
        try {
            console.log("# Functional Test server launched.");
            await runTests(webDriverPort, results[1]);
            cb();
        } catch (e) {
            console.error(`Bail out! Error running tests: ${e}`);
            cleanup(() => process.exit(1));
        }
    });
}
