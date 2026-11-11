/**
 * The Part 3 caching rule, pinned as a test: the service worker must NEVER
 * list /api (or any data/auth traffic) among the responses it may cache.
 * If the worker's never-cache list stops matching /api, or the fetch handler
 * stops routing those requests network-only, this test fails the build.
 */
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, resolve } from "node:path";
import { describe, expect, it } from "vitest";

const here = dirname(fileURLToPath(import.meta.url));
const swPath = resolve(here, "../public/sw.js");
const sw = readFileSync(swPath, "utf8");

describe("service worker caching rule", () => {
  it("documents the never-cache rule at the top of the file", () => {
    const head = sw.slice(0, 1200);
    expect(head).toMatch(/NEVER/i);
    expect(head).toContain("/api");
  });

  it("keeps /api in its never-cache list", () => {
    const list = sw.match(/NEVER_CACHE_PREFIXES\s*=\s*\[(.*?)\]/s);
    expect(list, "NEVER_CACHE_PREFIXES must exist in sw.js").not.toBeNull();
    expect(list![1]).toContain("/api");
  });

  it("routes never-cached requests to the network without touching any cache", () => {
    const branchStart = sw.indexOf("if (isNeverCached(");
    expect(branchStart).toBeGreaterThan(-1);
    const branch = sw.slice(branchStart, sw.indexOf("isShellAsset(url)", branchStart));
    expect(branch).toContain("fetch(");
    expect(branch).not.toContain("caches.");
    expect(branch).not.toContain("cache.put");
  });

  it("caches only static shell assets (bundles, fonts, icons)", () => {
    const matcher = sw.match(/function isShellAsset[\s\S]*?\{([\s\S]*?)\n\}/);
    expect(matcher, "isShellAsset must exist in sw.js").not.toBeNull();
    expect(matcher![1]).toMatch(/js\|css/);
    // No document, data or session-shaped pattern may be cacheable.
    expect(sw).not.toMatch(/cache\.put\(event\.request, response\.clone\(\)\)[\s\S]*\/api/);
  });
});
