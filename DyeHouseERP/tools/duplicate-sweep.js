/**
 * Duplicate-entity / duplicate-permission / duplicate-index sweep.
 *
 * These are the failure modes a compiler cannot see: two `const string` with
 * the same value, two `HasIndex` calls on the same property, an entity mapped
 * to a table that another entity already owns, or a DbSet that exists on the
 * concrete context but not on the interface the Application layer depends on.
 *
 * Run: node tools/duplicate-sweep.js
 */
const fs = require("fs");
const path = require("path");

const root = path.resolve(__dirname, "..");
const read = (p) => fs.readFileSync(path.join(root, p), "utf8");

let failures = 0;
const report = (label, offenders) => {
  if (offenders.length === 0) {
    console.log(`  OK    ${label}`);
  } else {
    failures += offenders.length;
    console.log(`  DUP   ${label}`);
    offenders.forEach((o) => console.log(`          ${o}`));
  }
};

function duplicates(values) {
  const seen = new Map();
  for (const v of values) seen.set(v, (seen.get(v) ?? 0) + 1);
  return [...seen].filter(([, n]) => n > 1).map(([v, n]) => `${v} (x${n})`);
}

// ------------------------------------------------------------- permissions

const permissions = read("src/DyeHouseERP.Domain/Common/Permissions.cs");
const permissionConsts = [...permissions.matchAll(/public const string (\w+)\s*=\s*"([^"]+)"/g)]
  .map((m) => `${m[1]} = ${m[2]}`);

console.log("permissions");
report("permission constants with a duplicated value", duplicates(permissionConsts.map((p) => p.split(" = ")[1])));

// ------------------------------------------------------------------- i18n

const i18n = read("src/DyeHouseERP.Web/src/i18n/index.tsx");
const dictionaries = [...i18n.matchAll(/(?:const|let)\s+(\w+)\s*(?::\s*Record<string, string>)?\s*=\s*\{([\s\S]*?)\n\};/g)];
console.log("\ni18n");
if (dictionaries.length < 2) {
  console.log("  WARN  could not detect two dictionaries - check i18n/index.tsx shape");
} else {
  for (const [, name, body] of dictionaries) {
    const keys = [...body.matchAll(/^\s*"([^"]+)":/gm)].map((m) => m[1]);
    report(`${name}: duplicated keys (${keys.length} total)`, duplicates(keys));
  }
  const [a, b] = dictionaries;
  const keysOf = (body) => new Set([...body.matchAll(/^\s*"([^"]+)":/gm)].map((m) => m[1]));
  const ka = keysOf(a[2]);
  const kb = keysOf(b[2]);
  report(`${b[1]} missing keys present in ${a[1]}`, [...ka].filter((k) => !kb.has(k)));
  report(`${a[1]} missing keys present in ${b[1]}`, [...kb].filter((k) => !ka.has(k)));
}

// --------------------------------------------------------------- entities

const entitiesDir = path.join(root, "src/DyeHouseERP.Domain/Entities");
const entityFiles = fs.readdirSync(entitiesDir).filter((f) => f.endsWith(".cs"));
console.log("\nentities");
report("entity classes with a duplicated name", duplicates(entityFiles.map((f) => f.replace(".cs", ""))));

// ---------------------------------------------------------------- indexes

const configsDir = path.join(root, "src/DyeHouseERP.Persistence/Configurations");
const configFiles = fs.readdirSync(configsDir).filter((f) => f.endsWith(".cs"));
console.log("\nindexes");
// A configuration file often maps several related entities, each with its own
// `HasIndex(l => l.MaterialId)`. Scope the check per `Entity<X>` block, or
// every line entity would be reported as a duplicate.
for (const file of configFiles) {
  const source = fs.readFileSync(path.join(configsDir, file), "utf8");
  const blocks = source.split(/builder\.Entity<\w+>\s*\(/)
    .slice(1)
    .map((block) => [
      ...block.matchAll(/HasIndex\(([^)]*)\)/g)
    ].map((m) => m[1].replace(/\s+/g, " ").replace(/[()]/g, "").trim()));

  for (const [index, indexes] of blocks.entries()) {
    const dupes = duplicates(indexes);
    if (dupes.length) report(`${file} block ${index + 1}: duplicated HasIndex`, dupes);
  }
}
console.log(`  OK    ${configFiles.length} entity configurations scanned`);

// ---------------------------------------------------------------- DbSets

const context = read("src/DyeHouseERP.Persistence/ApplicationDbContext.cs");
const iface = read("src/DyeHouseERP.Application/Common/Interfaces/IApplicationDbContext.cs");
const dbSetOf = (source) => new Set([...source.matchAll(/DbSet<(\w+)>\s+\w+/g)].map((m) => m[1]));
const concrete = dbSetOf(context);
const declared = dbSetOf(iface);

console.log("\nDbSets");
report("DbSet on the context but missing from IApplicationDbContext", [...concrete].filter((d) => !declared.has(d)));
report("DbSet on IApplicationDbContext but missing from the context", [...declared].filter((d) => !concrete.has(d)));
console.log(`  INFO  ${concrete.size} DbSets on the context, ${declared.size} on the interface`);

console.log(failures === 0 ? "\nNo duplicates found." : `\n${failures} duplicate(s) found.`);
process.exit(failures === 0 ? 0 : 1);
