/**
 * i18n parity check (Arabic <-> English).
 *
 * The translation file holds two literal dictionaries (`ar` and `en`). A key that
 * exists in only one of them silently falls back to the key itself at runtime, which
 * is invisible in a compile and obvious to a user. This script compares the two key
 * sets and fails when they differ.
 *
 * Run: node tools/i18n-parity-check.js
 */
const fs = require("fs");
const path = require("path");

const file = path.join(__dirname, "..", "src", "DyeHouseERP.Web", "src", "i18n", "index.tsx");
const source = fs.readFileSync(file, "utf8");

function keysOf(blockName) {
  const start = source.indexOf(`const ${blockName}:`);
  if (start < 0) throw new Error(`translation block "${blockName}" not found`);
  const open = source.indexOf("{", start);
  let depth = 0;
  let end = -1;
  for (let i = open; i < source.length; i++) {
    if (source[i] === "{") depth++;
    else if (source[i] === "}") {
      depth--;
      if (depth === 0) {
        end = i;
        break;
      }
    }
  }
  const body = source.slice(open + 1, end);
  const keys = new Set();
  // Only top-level keys: two-space indent inside the block.
  for (const line of body.split("\n")) {
    const m = line.match(/^ {2}"([^"]+)"\s*:/);
    if (m) keys.add(m[1]);
  }
  return keys;
}

const ar = keysOf("ar");
const en = keysOf("en");

const missingInEn = [...ar].filter((k) => !en.has(k));
const missingInAr = [...en].filter((k) => !ar.has(k));

console.log(`ar keys: ${ar.size}`);
console.log(`en keys: ${en.size}`);
if (missingInEn.length) console.log(`missing in en: ${missingInEn.join(", ")}`);
if (missingInAr.length) console.log(`missing in ar: ${missingInAr.join(", ")}`);

const failures = missingInEn.length + missingInAr.length;
console.log(`failures: ${failures}`);
process.exit(failures === 0 ? 0 : 1);