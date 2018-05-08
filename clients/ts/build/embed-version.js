const path = require("path");
const fs = require("fs");
const pkg = require(path.resolve(process.cwd(), "package.json"));

function processDirectory(dir) {
    for (const item of fs.readdirSync(dir)) {
        const stats = fs.statSync(item);
        if (stats.isDirectory()) {
            processDirectory(item);
        } else if (stats.isFile()) {
            processFile(item);
        }
    }
}

const SEARCH_STRING = "0.0.0-DEV_VERSION";
/**
 * @param {string} file 
 */
function processFile(file) {
    if (file.endsWith(".js") || file.endsWith(".ts")) {
        let content = fs.readFileSync(file);
        content = content.toString().replace(SEARCH_STRING, pkg.version);
        fs.writeFileSync(file, content);
    }
}