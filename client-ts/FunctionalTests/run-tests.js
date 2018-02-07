#!/usr/bin/env node

// console.log messages should be prefixed with "#" to ensure stdout continues to conform to TAP (Test Anything Protocol)
// https://testanything.org/tap-version-13-specification.html

const { spawn, spawnSync } = require("child_process");
const os = require("os");
const path = require("path");

const argv = require("yargs").argv;
const { Builder, By, logging, Capabilities } = require("selenium-webdriver");
const kill = require("tree-kill");

const rootDir = __dirname;

let verbose = argv.v || argv.verbose || false;
let browser = argv.browser || "chrome";
let headless = argv.headless || argv.h || false;

console.log("TAP version 13");

function logverbose(message) {
    if (verbose) {
        console.log(message);
    }
}

function runCommand(command, args) {
    args = args || [];
    let result = spawnSync(command, args, {
        cwd: rootDir
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

const logExtractorRegex = /[^ ]+ [^ ]+ "(.*)"/

function getMessage(logMessage) {
    let r = logExtractorRegex.exec(logMessage);

    // Unescape \"
    if (r && r.length >= 2) {
        return r[1].replace(/\\"/g, "\"");
    } else {
        return logMessage;
    }
}

async function waitForElement(driver, id) {
    while (true) {
        let elements = await driver.findElements(By.id(id));
        if (elements && elements.length > 0) {
            return elements[0];
        }
    }
}

async function isComplete(element) {
    return (await element.getAttribute("data-done")) === "1";
}

async function getLogEntry(index, element) {
    let elements = await element.findElements(By.id(`__tap_item_${index}`));
    if (elements && elements.length > 0) {
        return elements[0];
    }
    return null;
}

async function getEntryContent(element) {
    return await element.getAttribute("innerHTML");
}

async function flushEntries(index, element) {
    let entry = await getLogEntry(index, element);
    while (entry) {
        index += 1;
        console.log(await getEntryContent(entry));
        entry = await getLogEntry(index, element);
    }
}

function applyCapabilities(browser, builder) {
    if (browser === "chrome") {
        let caps = Capabilities.chrome();
        let args = [];

        if (headless) {
            console.log("# Using Headless Mode");
            args.push("--headless");
            if (process.platform === "win32") {
                args.push("--disable-gpu");
            }
        }

        caps.set("chromeOptions", {
            args: args
        });
        builder.withCapabilities(caps);
    }
}

async function runTests(port, serverUrl) {
    let webDriverUrl = `http://localhost:${port}/wd/hub`;
    console.log(`# Using WebDriver at ${webDriverUrl}`);
    console.log(`# Launching ${browser} browser`);
    let logPrefs = new logging.Preferences();
    logPrefs.setLevel(logging.Type.BROWSER, logging.Level.INFO);

    let builder = new Builder()
        .usingServer(webDriverUrl)
        .setLoggingPrefs(logPrefs)
        .forBrowser(browser);

    applyCapabilities(browser, builder);

    let driver = await builder.build();
    try {
        await driver.get(serverUrl);

        let index = 0;
        console.log("# Running tests");
        let element = await waitForElement(driver, "__tap_list");
        let success = true;
        while (!await isComplete(element)) {
            let entry = await getLogEntry(index, element);
            if (entry) {
                index += 1;
                console.log(await getEntryContent(entry));
            }
        }

        // Flush remaining entries
        await flushEntries(index, element);
        console.log("# End of tests");
    }
    catch (e) {
        console.error("Error: " + e.toString());
    }
    finally {
        await driver.quit();
    }
}

function waitForMatch(command, process, stream, regex, onMatch) {
    let lastLine = "";
    async function onData(chunk) {
        chunk = chunk.toString();

        // Process lines
        let lineEnd = chunk.indexOf(os.EOL);
        while (lineEnd >= 0) {
            let chunkLine = lastLine + chunk.substring(0, lineEnd);
            lastLine = "";

            chunk = chunk.substring(lineEnd + os.EOL.length);

            logverbose(`# ${command}: ${chunkLine}`);
            let results = regex.exec(chunkLine);
            if (results && results.length > 0) {
                onMatch(results);
                stream.removeAllListeners("data");
                return;
            }
            lineEnd = chunk.indexOf(os.EOL);
        }
        lastLine = chunk.toString();
    }

    process.on("close", (code, signal) => {
        console.log(`# ${command} process exited with code: ${code}`);
    })

    stream.on("data", onData);
}


let webDriverManagerPath = path.resolve(__dirname, "node_modules", "webdriver-manager", "bin", "webdriver-manager");

// This script launches the functional test app and then uses Selenium WebDriver to run the tests and verify the results.
console.log("# Updating WebDrivers...");
runCommand(process.execPath, [webDriverManagerPath, "update"]);
console.log("# Updated WebDrivers");

let webDriver;
let dotnet;

function cleanupDotNet(cb) {
    if (dotnet && !dotnet.killed) {
        console.log(`# Killing dotnet process (PID: ${dotnet.pid})`);
        kill(dotnet.pid, () => {
            console.log("# Killed dotnet process");
            cb();
        });
    }
    else {
        cb();
    }
}

function cleanup(cb) {
    if (webDriver && !webDriver.killed) {
        console.log(`# Killing webdriver-manager process (PID: ${webDriver.pid})`);
        kill(webDriver.pid, () => {
            console.log("# Killed webdriver-manager process");
            cleanupDotNet(cb)
        });
    } else {
        cleanupDotNet(cb);
    }
}

console.log("# Launching WebDriver...");
webDriver = spawn(process.execPath, [webDriverManagerPath, "start"]);

const regex = /\d+:\d+:\d+.\d+ INFO - Selenium Server is up and running on port (\d+)/;

// The message we're waiting for is written to stderr for some reason
waitForMatch("webdriver-server", webDriver, webDriver.stderr, regex, (results) => {
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

function webDriverLaunched(webDriverPort, cb) {
    console.log("# Launching Functional Test server...");
    dotnet = spawn("dotnet", [path.resolve(__dirname, "bin", "Debug", "netcoreapp2.1", "FunctionalTests.dll")], {
        cwd: rootDir,
    });

    const regex = /Now listening on: (http:\/\/localhost:([\d])+)/;
    waitForMatch("dotnet", dotnet, dotnet.stdout, regex, async (results) => {
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