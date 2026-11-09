/**
 * Where the UI smoke scripts find Puppeteer.
 *
 * Puppeteer downloads its own browser, so it is not a project dependency (it would make every
 * `npm ci` - including CI - pull a ~150 MB Chromium). It is instead installed once, on demand:
 *
 *     cd src/DyeHouseERP.Web && npm run smoke:install
 *
 * which puts it in `tools/.puppeteer` (git-ignored). `npm run smoke`, `smoke:authed` and
 * `smoke:tabs` all resolve it through here, in this order:
 *
 *   1. $PUPPETEER_DIR/node_modules/puppeteer   - an install kept somewhere else
 *   2. tools/.puppeteer/node_modules/puppeteer - what `npm run smoke:install` creates
 *   3. /tmp/uitest/node_modules/puppeteer      - the historical ad-hoc location
 *   4. the web app's own node_modules          - `npm i puppeteer` inside src/DyeHouseERP.Web
 *   5. a plain `require("puppeteer")`
 *
 * Nothing found exits with code 2 and the command to run, never a stack trace.
 */
const path = require("path");

const TOOLS_DIR = __dirname;
const INSTALL_DIR = path.join(TOOLS_DIR, ".puppeteer");

const HINT = [
  "Puppeteer is not installed, so the UI smoke test cannot run.",
  "Install it once with:",
  "  cd src/DyeHouseERP.Web && npm run smoke:install",
  "or install it anywhere and point PUPPETEER_DIR at that folder:",
  "  npm i puppeteer@24 && PUPPETEER_DIR=$(pwd) node ../../tools/ui-smoke.js"
].join("\n");

function candidates() {
  return [
    process.env.PUPPETEER_DIR ? path.join(process.env.PUPPETEER_DIR, "node_modules", "puppeteer") : null,
    path.join(INSTALL_DIR, "node_modules", "puppeteer"),
    "/tmp/uitest/node_modules/puppeteer",
    path.join(TOOLS_DIR, "..", "src", "DyeHouseERP.Web", "node_modules", "puppeteer"),
    "puppeteer"
  ].filter(Boolean);
}

/** The installed Puppeteer module, or exit 2 with the install command. */
function loadPuppeteer() {
  for (const candidate of candidates()) {
    try {
      return require(candidate);
    } catch {
      // Try the next location; the message below names the supported install.
    }
  }

  console.error(HINT);
  process.exit(2);
}

module.exports = { loadPuppeteer, INSTALL_DIR };
