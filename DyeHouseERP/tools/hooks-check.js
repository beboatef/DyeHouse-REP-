/**
 * React runtime-defect check, using the TypeScript compiler that is already
 * installed in the web project (no new dependency, no config file).
 *
 * These are the failure modes that `tsc --noEmit` passes straight through and
 * that only show up in a browser as a white screen:
 *
 *   1. a hook called inside an `if` / `&&` / ternary / loop / early return, or
 *      inside a callback or nested function - React throws
 *      "Invalid hook call" or "Rendered more hooks than during the previous
 *      render";
 *   2. a local variable that shadows a hook name (`const useState = ...`,
 *      `const useMemo = props.x`), which re-points the identifier and produces
 *      a baffling runtime error;
 *   3. `useState` / `useMemo` / `useCallback` assigned to or redefined as a
 *      plain local declaration, i.e. a homemade hook.
 *
 * Run: node tools/hooks-check.js
 */
const fs = require("fs");
const path = require("path");
const ts = require(path.join(__dirname, "..", "src/DyeHouseERP.Web/node_modules/typescript"));

const webSrc = path.join(__dirname, "..", "src/DyeHouseERP.Web/src");

function walk(dir, acc = []) {
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) walk(full, acc);
    else if (/\.tsx?$/.test(entry.name)) acc.push(full);
  }
  return acc;
}

const HOOK = /^(use[A-Z]\w*)$/;
const problems = [];

/** Names bound anywhere in the file (params, destructuring, declarations). */
function collectBindings(text) {
  const names = new Set();
  for (const m of text.matchAll(/\b(?:const|let|var|function|class)\s+([A-Za-z_$][\w$]*)/g)) names.add(m[1]);
  for (const m of text.matchAll(/\(([^)]*)\)\s*=>/g)) {
    for (const part of m[1].split(",")) {
      const n = part.split(":").pop().split("=")[0].trim().replace(/^\.\.\./, "");
      if (n) names.add(n);
    }
  }
  return names;
}

for (const file of walk(webSrc)) {
  const sourceText = fs.readFileSync(file, "utf8");
  const source = ts.createSourceFile(file, sourceText, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);
  const rel = path.relative(webSrc, file);
  const bindings = collectBindings(sourceText);

  // (2) a local binding shadowing a hook name. A project's own `export function
  // useSomething` is a legitimate custom hook, not a shadow - only flag
  // declarations that are not exported and are not hook-shaped functions.
  const ownCustomHooks = new Set(
    [...sourceText.matchAll(/export\s+(?:function|const)\s+(use[A-Z]\w*)/g)].map((m) => m[1])
  );
  for (const name of bindings) {
    if (HOOK.test(name) && !ownCustomHooks.has(name)) {
      problems.push(`${rel}: local declaration shadows the hook '${name}'`);
    }
  }

  function visit(node) {
    // (1) hook calls in a non-top-level position
    if (ts.isCallExpression(node) && ts.isIdentifier(node.expression) && HOOK.test(node.expression.text)) {
      const hook = node.expression.text;

      let cur = node.parent;
      let offender = null;

      while (cur) {
        if (ts.isIfStatement(cur) || ts.isForStatement(cur) || ts.isForInStatement(cur) ||
            ts.isForOfStatement(cur) || ts.isWhileStatement(cur) || ts.isDoStatement(cur) ||
            ts.isConditionalExpression(cur) || ts.isCaseClause(cur) ||
            (ts.isBinaryExpression(cur) && cur.operatorToken.kind === ts.SyntaxKind.AmpersandAmpersandToken)) {
          offender = "conditional/loop";
          break;
        }
        if (ts.isArrowFunction(cur) || ts.isFunctionExpression(cur)) {
          offender = "callback";
          break;
        }
        if (ts.isFunctionDeclaration(cur) || ts.isMethodDeclaration(cur)) break;
        cur = cur.parent;
      }

      if (offender) {
        const { line } = source.getLineAndCharacterOfPosition(node.getStart());
        problems.push(`${rel}:${line + 1}: hook '${hook}' called in an ${offender} position`);
      }
    }
    ts.forEachChild(node, visit);
  }

  visit(source);
}

if (problems.length === 0) {
  console.log("No invalid hook call sites, no hook shadowing.");
  process.exit(0);
}

console.log(`${problems.length} potential runtime defect(s):`);
problems.forEach((p) => console.log("  " + p));
process.exit(1);
