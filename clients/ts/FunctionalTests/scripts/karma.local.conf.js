// Karma configuration for a local run (the default)
const createKarmaConfig = require("./karma.base.conf");
const fs = require("fs");

// Bring in the launchers directly to detect browsers
const ChromeHeadlessBrowser = require("karma-chrome-launcher")["launcher:ChromeHeadless"][1];
const ChromiumHeadlessBrowser = require("karma-chrome-launcher")["launcher:ChromiumHeadless"][1];
const FirefoxHeadlessBrowser = require("karma-firefox-launcher")["launcher:FirefoxHeadless"][1];
const EdgeBrowser = require("karma-edge-launcher")["launcher:Edge"][1];
const SafariBrowser = require("karma-safari-launcher")["launcher:Safari"][1];
const IEBrowser = require("karma-ie-launcher")["launcher:IE"][1];

let browsers = [];

function tryAddBrowser(name, b) {
  var path = b.DEFAULT_CMD[process.platform];
  if (b.ENV_CMD && process.env[b.ENV_CMD]) {
    path = process.env[b.ENV_CMD];
  }
  if (path && fs.existsSync(path)) {
    console.log(`Located ${name} at ${path}.`);
    browsers.push(name);
  }
  else {
    console.log(`Unable to locate ${name}. Skipping.`);
  }
}

// Hacky AF way to use the launcher itself to detect the browser.
tryAddBrowser("ChromeHeadless", new ChromeHeadlessBrowser(() => { }, {}));
tryAddBrowser("ChromiumHeadless", new ChromiumHeadlessBrowser(() => { }, {}));
tryAddBrowser("FirefoxHeadless", new FirefoxHeadlessBrowser(0, () => { }, {}));

// Hacky AF, but this is how the script that runs us can pass us args :(.
if (process.env.ASPNETCORE_SIGNALR_TEST_ALL_BROWSERS === "true") {
  tryAddBrowser("Edge", new EdgeBrowser(() => { }, { create() { } }));
  tryAddBrowser("IE", new IEBrowser(() => { }, { create() { } }, {}));
  tryAddBrowser("Safari", new SafariBrowser(() => { }, {}));
}

module.exports = createKarmaConfig({
  browsers,
});