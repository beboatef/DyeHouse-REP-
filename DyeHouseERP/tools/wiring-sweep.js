/**
 * Structural verification for the API <-> UI contract.
 *
 * Three things get checked, none of which a build can catch:
 *   1. every download/import path the React app calls resolves to a real
 *      controller action (a UI button pointing at nothing is worse than none);
 *   2. every controller action carries its own permission policy, so there is
 *      no endpoint that silently inherits class-level access;
 *   3. no duplicate (verb, route) pair anywhere.
 *
 * Run: node tools/wiring-sweep.js
 */
const fs = require("fs");
const path = require("path");

const root = path.resolve(__dirname, "..");
const controllersDir = path.join(root, "src/DyeHouseERP.API/Controllers");
const webSrc = path.join(root, "src/DyeHouseERP.Web/src");

// --------------------------------------------------------------- backend routes

/** @type {{verb:string, route:string, file:string, line:number, hasAuth:boolean}[]} */
const actions = [];

for (const file of fs.readdirSync(controllersDir).filter((f) => f.endsWith(".cs"))) {
  const full = path.join(controllersDir, file);
  const lines = fs.readFileSync(full, "utf8").split("\n");
  let route = null;

  for (let i = 0; i < lines.length; i++) {
    const routeMatch = lines[i].match(/\[Route\("([^"]+)"\)\]/);
    if (routeMatch) {
      route = routeMatch[1].replace("[controller]", file.replace(".cs", "").replace("Controller", "").toLowerCase());
      continue;
    }

    const http = lines[i].match(/\[Http(Get|Post|Put|Delete)(?:\("([^"]*)"\))?\]/);
    if (!http || !route) continue;

    // Walk forward to the method signature, collecting attributes above it.
    let signature = null;
    for (let j = i + 1; j < Math.min(i + 16, lines.length); j++) {
      if (/^\s{4}public\s/.test(lines[j])) {
        signature = lines[j].trim();
        break;
      }
    }
    if (!signature) continue;

    // The attributes sit *between* the [Http...] line and the method
    // signature, so walk forward from the [Http] line, not backward.
    const hasAuth = (function () {
      for (let j = i + 1; j < Math.min(i + 16, lines.length); j++) {
        if (/^\s{4}public\s/.test(lines[j])) return false;
        if (/\[Authorize\(Policy/.test(lines[j])) return true;
      }
      return false;
    })();

    actions.push({
      verb: http[1].toUpperCase(),
      route: (route + (http[2] ? "/" + http[2] : "")).replace(/\{[^}]+\}/g, "{}"),
      file,
      line: i + 1,
      hasAuth,
      signature
    });
  }
}

const routeKey = (a) => `${a.verb} ${a.route}`;

// --------------------------------------------------------------- frontend calls

function walk(dir, acc = []) {
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) walk(full, acc);
    else if (/\.(ts|tsx)$/.test(entry.name)) acc.push(full);
  }
  return acc;
}

const files = walk(webSrc);

/** Calls that stream a file or upload a workbook. */
const DOWNLOADERS = /(?:downloadFile|download|upload)\(\s*[`"'](\/[^`'"\s]*)/g;

const calls = [];
for (const file of files) {
  const source = fs.readFileSync(file, "utf8");
  for (const match of source.matchAll(DOWNLOADERS)) {
    calls.push({ file: path.relative(webSrc, file), raw: match[1] });
  }
  // The shared import wizard builds its URLs as `${base}/template` etc, so the
  // base has to be checked separately or a typo'd prefix would ship silently.
  for (const match of source.matchAll(/<ImportPanel([\s\S]{0,400}?)\/>/g)) {
    const base = match[1].match(/base="([^"]+)"/);
    if (!base) continue;
    const rel = path.relative(webSrc, file);
    for (const step of ["/template", "/preview", "/execute"]) {
      calls.push({ file: rel, raw: `${base[1]}${step}` });
    }
    // `-update` is only reachable when the panel was not declared
    // create-only, so only then must the backend expose it.
    if (!/supportsUpdate=\{false\}/.test(match[1])) {
      calls.push({ file: rel, raw: `${base[1]}/preview-update` });
      calls.push({ file: rel, raw: `${base[1]}/execute-update` });
    }
  }
}

/**
 * `/customers/${id}/statement/pdf` -> `api/customers/{}/statement/pdf`
 *
 * The axios instance is mounted on `/api`, so every UI path is relative to it
 * while controller routes carry the prefix literally - normalise both sides.
 */
function normalise(call) {
  const trimmed = call.split("?")[0].replace(/\$\{[^}]*\}/g, "{}").replace(/\/+$/, "");
  const withoutSlash = trimmed.startsWith("/") ? trimmed.slice(1) : trimmed;
  return withoutSlash.startsWith("api/") ? withoutSlash : "api/" + withoutSlash;
}

const known = new Set(actions.map(routeKey));
const unresolved = [];

for (const call of calls) {
  const path_ = normalise(call.raw);
  // Downloads are GET and uploads are POST, but an import path shared by both
  // (template = GET, preview/execute = POST) should not be flagged for verb.
  const ok =
    known.has(`GET ${path_}`) ||
    known.has(`POST ${path_}`) ||
    [...known].some((key) => key.endsWith(` ${path_}`));
  if (!ok) unresolved.push(`${call.file}  ->  ${call.raw}  (${path_})`);
}

// An import wizard may only offer the *-update pair when the backend actually
// has it - otherwise the checkbox silently 404s.
const updateUnbacked = [];
for (const file of files) {
  const source = fs.readFileSync(file, "utf8");
  for (const match of source.matchAll(/<ImportPanel([\s\S]{0,400}?)\/>/g)) {
    if (/supportsUpdate=\{false\}/.test(match[1])) continue;
    const base = match[1].match(/base="([^"]+)"/);
    if (!base) continue;
    const key = `POST api${base[1]}/execute-update`;
    if (!known.has(key)) updateUnbacked.push(`${path.relative(webSrc, file)}  ->  ${key}`);
  }
}

// ------------------------------------------------------------------- reporting

const duplicates = new Map();
for (const a of actions) {
  const key = routeKey(a);
  if (!duplicates.has(key)) duplicates.set(key, []);
  duplicates.get(key).push(`${a.file}:${a.line}`);
}
const duplicateRoutes = [...duplicates].filter(([, v]) => v.length > 1);

const unauthenticated = actions.filter((a) => !a.hasAuth);

console.log(`controller actions      : ${actions.length}`);
console.log(`distinct verb+route     : ${duplicates.size}`);
console.log(`duplicate routes        : ${duplicateRoutes.length}`);
console.log(`actions without a policy: ${unauthenticated.length}`);
console.log(`UI file calls checked   : ${calls.length}`);
console.log(`UI calls unresolved     : ${unresolved.length}`);
console.log(`update-imports unbacked : ${updateUnbacked.length}`);

if (duplicateRoutes.length) {
  console.log("\nDUPLICATE ROUTES");
  duplicateRoutes.forEach(([key, where]) => console.log(`  ${key}  ->  ${where.join(", ")}`));
}

if (unauthenticated.length) {
  console.log("\nACTIONS WITHOUT A PERMISSION POLICY (expected: login + public settings)");
  unauthenticated.forEach((a) => console.log(`  ${a.file}:${a.line}  ${routeKey(a)}`));
}

if (unresolved.length) {
  console.log("\nUNRESOLVED UI CALLS");
  unresolved.forEach((u) => console.log(`  ${u}`));
}

if (updateUnbacked.length) {
  console.log("\nIMPORT PANELS OFFERING UPDATE WITH NO BACKEND ENDPOINT");
  updateUnbacked.forEach((u) => console.log(`  ${u}`));
}

process.exit(unresolved.length || updateUnbacked.length || duplicateRoutes.length ? 1 : 0);
