/**
 * Permission parity check (backend Domain <-> frontend permissions.ts).
 *
 * The backend is the single source of truth for permission strings
 * (DyeHouseERP.Domain.Common.Permissions). The frontend keeps a mirrored list so
 * administrators can assign granular rights in the UI. A value that exists in
 * only one of the two sides is a silent defect: a backend-only permission can
 * never be granted to a non-admin user, and a frontend-only value is dead.
 *
 * This mirrors tools/i18n-parity-check.cjs: it compares the two sets and fails
 * (exit 1) when they differ, so drift is caught without anyone having to
 * remember to update both files.
 *
 * Run: node tools/permissions-parity-check.cjs
 */
const fs = require("fs");
const path = require("path");

const repoRoot = path.join(__dirname, "..");
const backendFile = path.join(repoRoot, "src", "DyeHouseERP.Domain", "Common", "Permissions.cs");
const frontendFile = path.join(repoRoot, "src", "DyeHouseERP.Web", "src", "permissions.ts");

const backendSource = fs.readFileSync(backendFile, "utf8");
const frontendSource = fs.readFileSync(frontendFile, "utf8");

// Backend: every `public const string Name = "value";` (the value IS the claim).
const backend = new Set();
for (const m of backendSource.matchAll(/public const string \w+\s*=\s*"([^"]+)"/g)) backend.add(m[1]);

// Frontend: every entry `{ value: "value", group: ..., label: ... }`.
const frontend = new Set();
for (const m of frontendSource.matchAll(/\{\s*value:\s*"([^"]+)"/g)) frontend.add(m[1]);

const missingInFrontend = [...backend].filter((v) => !frontend.has(v)).sort();
const missingInBackend = [...frontend].filter((v) => !backend.has(v)).sort();

console.log(`backend permissions : ${backend.size}`);
console.log(`frontend permissions: ${frontend.size}`);
if (missingInFrontend.length) console.log(`missing in frontend (permissions.ts): ${missingInFrontend.join(", ")}`);
if (missingInBackend.length) console.log(`missing in backend (Permissions.cs): ${missingInBackend.join(", ")}`);

const failures = missingInFrontend.length + missingInBackend.length;
console.log(`failures: ${failures}`);
process.exit(failures === 0 ? 0 : 1);
