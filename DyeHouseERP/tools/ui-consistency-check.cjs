/**
 * UI consistency check (the design language of the dashboard redesign).
 *
 * The redesign moved every screen onto ONE set of tokens and shared classes.
 * That only stays true if drift is caught mechanically, because the failure
 * mode is invisible in a compile: a new page using `text-gray-800` or a card
 * with hand-written `p-4` still builds, still renders, and silently breaks the
 * palette, the spacing and (for the raw palette) dark mode.
 *
 * Rules, and why each one exists:
 *   1. TOKENS      - the canvas is #F4F6FB, the accent is the vibrant #2b5cff
 *                    and a card corner is 16px. If a token is retuned by
 *                    accident, every screen changes at once; this pins them.
 *   2. PALETTE     - no Tailwind stock palette class (`bg-gray-50`,
 *                    `text-red-600`, `border-yellow-300`, ...). Those resolve
 *                    to fixed literals the theme cannot repaint, which is how
 *                    a dark-mode screen ended up with a white-yellow banner.
 *   3. WHITE/BLACK - `white`/`black` are remapped onto theme surfaces, so they
 *                    are legal as a surface but never as text on a coloured
 *                    fill (that is what `.on-brand` is for). Neither name may
 *                    be used as a utility anywhere.
 *   4. HEX         - no literal colour inside a className, except the two
 *                    documented cases (the login hero panel and the drawer
 *                    scrim, both painted on a fixed dark surface).
 *   5. CARD PAD    - a <Card> pads itself with `card-pad`, never with a
 *                    hand-written `p-4`/`p-5`, so one card cannot be tighter
 *                    than its neighbours.
 *   6. HEADINGS    - panel headings are `card-title`/`section-title`, not an
 *                    ad-hoc `text-sm font-bold text-ink`.
 *   7. SEARCH      - a search field is <SearchInput> (magnifier, offset and
 *                    filled style come from one definition).
 *   8. CARD SHAPE  - `.card` is a 16px corner with `shadow-card`.
 *   9. PAPER       - the printable sheet pins the light palette via `.paper`,
 *                    so printing from dark mode cannot produce a dark page.
 *
 * Run: node tools/ui-consistency-check.cjs
 * Exit code 1 when any rule fails, so CI and the pre-commit habit both work.
 */
const fs = require("fs");
const path = require("path");

const repoRoot = path.join(__dirname, "..");
const webRoot = path.join(repoRoot, "src", "DyeHouseERP.Web");
const srcRoot = path.join(webRoot, "src");
const cssFile = path.join(srcRoot, "index.css");
const tailwindFile = path.join(webRoot, "tailwind.config.js");
const printLayout = path.join(srcRoot, "components", "PrintPreviewLayout.tsx");

const failures = [];
const fail = (rule, message) => failures.push(`${rule}: ${message}`);

/* ------------------------------------------------------------ file discovery */
const files = [];
(function walk(dir) {
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) walk(full);
    else if (/\.tsx$/.test(entry.name)) files.push(full);
  }
})(srcRoot);

const rel = (f) => path.relative(repoRoot, f).replace(/\\/g, "/");

/* --------------------------------------------------------------- 1. tokens */
const css = fs.readFileSync(cssFile, "utf8");
const tailwind = fs.readFileSync(tailwindFile, "utf8");

const declaredIn = (name) => css.split(new RegExp(`\\s${name}:`, "g")).length - 1;

/** Colours: declared in the light scope AND overridden by `.dark`. */
const COLOR_TOKENS = [
  ["--surface-sunken", "#f4f6fb;", "the app canvas is #F4F6FB"],
  ["--brand-600", "#2b5cff;", "the accent is the vibrant #2b5cff"]
];
for (const [name, value, why] of COLOR_TOKENS) {
  const count = declaredIn(name);
  if (count < 2) fail("TOKENS", `index.css must declare \`${name}\` for both themes (${why}); found ${count}`);
  if (!css.includes(`${name}: ${value}`)) fail("TOKENS", `index.css light scope must declare \`${name}: ${value}\` (${why})`);
}

/** Shape: theme-independent, declared once. */
const SHAPE_TOKENS = [["--radius-card", "16px;", "a card corner is 16px"]];
for (const [name, value, why] of SHAPE_TOKENS) {
  if (!css.includes(`${name}: ${value}`)) fail("TOKENS", `index.css must declare \`${name}: ${value}\` (${why})`);
}

if (!/--shadow-primary:/.test(css)) fail("TOKENS", "index.css must define the accent glow token --shadow-primary");
if (!/"2xl": "var\(--radius-card\)"/.test(tailwind)) {
  fail("TOKENS", "tailwind.config.js must map rounded-2xl to var(--radius-card)");
}
if (!/\bprimary:\s*"var\(--shadow-primary\)"/.test(tailwind)) {
  fail("TOKENS", "tailwind.config.js must expose shadow-primary for the accent glow");
}

/* ------------------------------------------- 2,3,4. palette / white / hex */
const STOCK_PALETTE =
  /\b(?:[a-z-]+:)*(?:bg|text|border|ring|divide|from|to|via|fill|stroke|decoration|outline)(?:-[trblsexy])?-(?:gray|slate|zinc|neutral|stone|red|rose|pink|green|emerald|teal|lime|amber|yellow|orange|blue|sky|cyan|indigo|violet|purple)-\d{2,3}\b/;
const WHITE_BLACK = /\b(?:[a-z-]+:)*(?:bg|text|border|ring|divide|from|to|via|fill|stroke)-(?:white|black)(?:\/\d{1,3})?\b/;
const HEX_IN_CLASS = /className=(?:"[^"]*"|\{`[^`]*`\})/g;
const HEX = /#[0-9a-fA-F]{3,8}\b/;

/** Files where a literal colour is deliberate, each for a stated reason. */
const HEX_ALLOWED = {
  "src/DyeHouseERP.Web/src/components/BrandLogo.tsx": "the animated brand artwork's own gradient stops",
  "src/DyeHouseERP.Web/src/pages/LoginPage.tsx":
    "a fixed dark hero panel: its white grid and white ink must not follow the theme",
  "src/DyeHouseERP.Web/src/components/Layout.tsx":
    "the mobile drawer's scrim, which must stay dark in both themes"
};

for (const file of files) {
  const text = fs.readFileSync(file, "utf8");
  const name = rel(file);
  const lines = text.split("\n");

  lines.forEach((line, i) => {
    const at = `${name}:${i + 1}`;
    if (STOCK_PALETTE.test(line)) fail("PALETTE", `${at} uses a stock palette class: ${line.trim().slice(0, 110)}`);
    if (WHITE_BLACK.test(line)) fail("WHITE/BLACK", `${at} uses white/black as a utility (use a token, or .on-brand for text on a fill)`);
  });

  if (!HEX_ALLOWED[name]) {
    for (const match of text.match(HEX_IN_CLASS) || []) {
      if (HEX.test(match)) {
        fail("HEX", `${name} has a literal colour inside a className: ${match.slice(0, 110)}`);
      }
    }
  }
}

/* -------------------------------------------------------- 5. card padding */
const CARD_PADDING = /<Card className="[^"]*\bp-(?:3|4|5|6|8)\b[^"]*"/;
for (const file of files) {
  const text = fs.readFileSync(file, "utf8");
  if (CARD_PADDING.test(text)) {
    const hit = text.match(CARD_PADDING)[0];
    fail("CARD PADDING", `${rel(file)} pads a card by hand (use card-pad): ${hit.slice(0, 110)}`);
  }
}

/* ---------------------------------------------------------- 6. headings */
const AD_HOC_HEADING = /<(h[234])([^>]*?)className="[^"]*font-(?:bold|semibold)[^"]*text-(?:ink|ink-muted|ink-subtle)[^"]*"/;
for (const file of files) {
  const text = fs.readFileSync(file, "utf8");
  if (AD_HOC_HEADING.test(text)) {
    fail("HEADINGS", `${rel(file)} styles a heading by hand (use card-title / section-title)`);
  }
}

/* ------------------------------------------------------------ 7. search */
for (const file of files) {
  const text = fs.readFileSync(file, "utf8");
  for (const tag of text.match(/<Input\b[\s\S]*?\/>/g) || []) {
    if (/placeholder=\{t\("[^"]*(?:[sS]earch|ابحث)[^"]*"\)\}/.test(tag) || /aria-label=\{t\("common\.search"\)\}/.test(tag)) {
      fail("SEARCH", `${rel(file)} renders a search field as a bare <Input> (use SearchInput)`);
    }
  }
}

/* --------------------------------------------------------------- 9. tabs */
// A tab strip is one control: `.tabs` + `.tab` (+ `.tab-active`). The old
// hand-rolled version was an underlined row built out of `border-b-2 -mb-px`,
// which is why four pages' tabs looked nothing like the other four.
for (const file of files) {
  const text = fs.readFileSync(file, "utf8");
  if (/\b-mb-px\b/.test(text)) {
    fail("TABS", `${rel(file)} hand-rolls a tab strip (use the shared .tabs / .tab / .tab-active classes)`);
  }
}

/* -------------------------------------------------------- 10. card shape */
if (!/\.card\s*\{[^}]*rounded-2xl[^}]*shadow-card/s.test(css)) {
  fail("CARD SHAPE", "index.css `.card` must be rounded-2xl with shadow-card");
}
if (!/\.card-pad\s*\{[^}]*p-5/s.test(css)) {
  fail("CARD SHAPE", "index.css `.card-pad` must be the shared card padding");
}

/* ------------------------------------------------------------ 11. paper */
const printSource = fs.readFileSync(printLayout, "utf8");
if (!/className="paper /.test(printSource)) {
  fail("PAPER", "PrintPreviewLayout's sheet must carry the `paper` class so printing stays black-on-white in dark mode");
}
if (!/:root,\n  \.paper \{/.test(css)) {
  fail("PAPER", "index.css must extend the light token scope to .paper");
}

/* ---------------------------------------------------------------- report */
console.log(`ui files scanned : ${files.length}`);
if (failures.length === 0) {
  console.log("design consistency: OK");
  process.exit(0);
}
console.log(`failures: ${failures.length}`);
for (const f of failures) console.log(`  ${f}`);
process.exit(1);
