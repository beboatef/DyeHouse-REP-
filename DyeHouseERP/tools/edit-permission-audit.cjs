#!/usr/bin/env node
/**
 * Edit-permission audit.
 *
 * Answers two questions with evidence from the source tree:
 *
 *  1. AUTHENTICATION/AUTHORIZATION: for every mutating HTTP action (POST/PUT/PATCH/DELETE)
 *     on every controller, is access decided by a granular backend permission policy -
 *     rather than by an `admin` role gate, or by nothing at all?
 *
 *  2. HARD-CODED EDIT LOCKS: every place in the Application/Domain layers that refuses an
 *     edit or a delete (DocumentLockedException and explicit status guards). Each one is
 *     listed with its file:line and message so it can be classified as either
 *     "posted document - corrected through the reversal workflow" (justified) or
 *     "record is merely saved/started - restriction to remove" (unjustified).
 *
 * Read-only: prints a report, changes nothing.
 */
const fs = require("fs");
const path = require("path");

const root = path.resolve(__dirname, "..");
const controllersDir = path.join(root, "src/DyeHouseERP.API/Controllers");
const srcDir = path.join(root, "src");

function walk(dir, out = []) {
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      if (["bin", "obj", "node_modules"].includes(entry.name)) continue;
      walk(full, out);
    } else if (entry.name.endsWith(".cs")) {
      out.push(full);
    }
  }
  return out;
}

function relative(file) {
  return path.relative(root, file).split(path.sep).join("/");
}

// ---------------------------------------------------------------------------
// 1. Controller actions
// ---------------------------------------------------------------------------
const mutatingVerbs = ["HttpPost", "HttpPut", "HttpPatch", "HttpDelete"];
const readVerbs = ["HttpGet"];
const rows = [];

for (const file of fs.readdirSync(controllersDir).filter((f) => f.endsWith(".cs"))) {
  const full = path.join(controllersDir, file);
  const lines = fs.readFileSync(full, "utf8").split("\n");

  // Class-level attributes: anything before "public class".
  const classLineIdx = lines.findIndex((l) => /public\s+(sealed\s+)?class\s+\w+Controller/.test(l));
  const classAttributes = classLineIdx >= 0 ? lines.slice(0, classLineIdx).join("\n") : "";
  const classRoleGates = [...classAttributes.matchAll(/\[Authorize\([^\]]*Roles\s*=\s*"([^"]+)"[^\]]*\)\]/g)]
    .map((m) => m[1]);

  let pendingAttributes = [];
  for (let i = 0; i < lines.length; i++) {
    const line = lines[i].trim();
    if (line.startsWith("[") && !line.startsWith("[From")) {
      pendingAttributes.push(line);
      continue;
    }
    if (/^public\s/.test(line) && pendingAttributes.length > 0) {
      const attrs = pendingAttributes.join(" ");
      pendingAttributes = [];
      const verb = mutatingVerbs.find((v) => attrs.includes(`[${v}`));
      const readVerb = readVerbs.find((v) => attrs.includes(`[${v}`));
      if (!verb && !readVerb) continue;

      const permission = (attrs.match(/PermissionPolicy\.Prefix\s*\+\s*Permissions\.(\w+)/) || [])[1] || null;
      const roleGate = (attrs.match(/Roles\s*=\s*"([^"]+)"/) || [])[1] || null;
      const allowAnonymous = attrs.includes("AllowAnonymous");

      rows.push({
        file,
        line: i + 1,
        verb: (verb || readVerb).replace("Http", "").toUpperCase(),
        mutating: !!verb,
        permission,
        roleGate,
        allowAnonymous,
        signature: line.replace(/\s+/g, " ").slice(0, 110)
      });
      continue;
    }
    if (line === "" || line.startsWith("//") || line.startsWith("///")) continue;
    if (line.startsWith("[")) continue;
    pendingAttributes = [];
  }

  if (classRoleGates.length > 0) {
    console.log(`CLASS-LEVEL ROLE GATE  ${file}: Roles="${classRoleGates.join(',')}"`);
  }
}

const mutating = rows.filter((r) => r.mutating);
const unguarded = mutating.filter((r) => !r.permission && !r.allowAnonymous);
const anon = mutating.filter((r) => r.allowAnonymous);

console.log(`\nMutating actions: ${mutating.length}  (permission-guarded: ${mutating.length - unguarded.length - anon.length}, anonymous: ${anon.length}, UNGUARDED: ${unguarded.length})`);
for (const r of anon) console.log(`  ANONYMOUS  ${r.file}:${r.line}  ${r.signature}`);
for (const r of unguarded) console.log(`  UNGUARDED  ${r.file}:${r.line}  ${r.signature}`);

const roleGated = mutating.filter((r) => r.roleGate);
console.log(`Mutating actions behind an explicit role gate: ${roleGated.length}`);
for (const r of roleGated) console.log(`  ROLE GATE  ${r.file}:${r.line}  Roles="${r.roleGate}"  ${r.signature}`);

// Every distinct permission used by a mutating action.
const usedPermissions = [...new Set(mutating.map((r) => r.permission).filter(Boolean))].sort();
console.log(`\nDistinct permissions on mutating actions: ${usedPermissions.length}`);
console.log(usedPermissions.map((p) => `  ${p}`).join("\n"));

// ---------------------------------------------------------------------------
// 2. Hard-coded edit locks
// ---------------------------------------------------------------------------
const lockPatterns = [
  { name: "DocumentLockedException", re: /throw new DocumentLockedException\(/ },
  {
    name: "status edit refusal",
    re: /throw new DomainException\(\s*\$?"[^"]*(cannot be (edited|changed|modified|deleted|updated)|already (cancelled|completed|been)|is (Completed|Cancelled|Posted|Closed|Delivered)|once the order)[^"]*"/i
  }
];

const locks = [];
for (const file of walk(srcDir)) {
  const rel = relative(file);
  // Migration/snapshot files describe history and are never enforcement points.
  if (rel.includes("Migrations/")) continue;
  const text = fs.readFileSync(file, "utf8");
  if (!/throw new DocumentLockedException|throw new DomainException/.test(text)) continue;
  const lines = text.split("\n");
  lines.forEach((line, idx) => {
    for (const p of lockPatterns) {
      if (p.re.test(line)) {
        // Nearest preceding member declaration, so the report names the method, not just the line.
        let member = "";
        for (let back = idx; back >= 0 && back > idx - 60; back--) {
          const m = lines[back].match(/^\s*(public|private|internal|protected)[^;=]*\(([^)]*)\)/);
          if (m && !lines[back].includes("=>")) { member = lines[back].trim().replace(/\s+/g, " ").slice(0, 90); break; }
        }
        const message = (text.split("\n").slice(idx - 2, idx + 4).join(" ").match(/"([^"]{10,140})"/) || [])[1] || "";
        locks.push({ kind: p.name, file: rel, line: idx + 1, member, message, text: line.trim().slice(0, 120) });
        break;
      }
    }
  });
}

console.log(`\n\nHard-coded edit-lock sites: ${locks.length}`);
const byFile = {};
for (const l of locks) (byFile[l.file] ||= []).push(l);
for (const [file, items] of Object.entries(byFile).sort()) {
  console.log(`\n${file}`);
  for (const l of items) console.log(`  ${l.line}: [${l.kind}] ${l.member}\n      ${l.message || l.text}`);
}
