/**
 * Frontend <-> backend route contract.
 *
 * Every API path the web client can request must resolve to a real controller action:
 * a path with no matching `{HTTP method, route template}` on the backend is a guaranteed
 * 404, so the test fails listing each one with its call site. It also fails on any NEW
 * call site whose URL cannot be determined statically (see DYNAMIC_ALLOWLIST) - an
 * unresolvable call site cannot be verified, so it must be registered deliberately.
 *
 * How URLs are collected:
 * - `api.<method>("<literal>")` / backtick templates (`${...}` becomes a `{placeholder}`),
 * - first arguments of the download helpers (`downloadFile/downloadBlob/download/both`)
 *   which forward their URL straight into `api.get` (member calls like `X.download(...)`
 *   are NOT collected - their URL is built inside the callee, which is scanned itself),
 * - `ImportPanel` usages (base props) expanded with that component's real suffixes
 *   (GET .../template, POST .../preview|execute, plus -update variants unless the usage
 *   passes `supportsUpdate={false}` - matching ImportPanel.tsx).
 * Backend routes come from parsing `Controllers/*.cs`: class-level `[Route]` (with the
 * `[controller]` token resolved) + action-level `[HttpXxx("template")]`. ASP.NET Core
 * routing is case-insensitive, so comparison is too; `{id:guid}`-style constraints and
 * placeholder NAMES are ignored, placeholder POSITIONS are not.
 */
import { readdirSync, readFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";

const API_DIR = dirname(fileURLToPath(import.meta.url));
const WEB_SRC = resolve(API_DIR, "..");
const CONTROLLERS_DIR = resolve(API_DIR, "../../../DyeHouseERP.API/Controllers");

interface Endpoint {
  method: string;
  segments: string[];
  origin: string;
}

function splitSegments(path: string): string[] {
  return path
    .split("?")[0]
    .split("/")
    .map((s) => s.trim())
    .filter((s) => s.length > 0)
    .map((s) => {
      // A route parameter matches any route parameter, whatever its name or constraint.
      if (s.startsWith("{") && s.endsWith("}")) return "{p}";
      return s.toLowerCase();
    });
}

function lineOf(source: string, index: number): number {
  if (index <= 0) return 1;
  return source.slice(0, index).split("\n").length;
}

/** `${anything}` (even `${encodeURIComponent(x)}`) becomes a path placeholder. */
function materializeTemplate(literal: string): string {
  return literal.replace(/\$\{[^}]*\}/g, "{p}");
}

/** `cond ? "litA" : "litB"` - a static choice between two URLs: both are validated. */
const TERNARY_URLS =
  /^\s*[^?,()"'\n]+\?\s*("(?:[^"\\]|\\.)*"|'(?:[^'\\]|\\.)*'|`(?:[^`\\]|\\.)*`)\s*:\s*("(?:[^"\\]|\\.)*"|'(?:[^'\\]|\\.)*'|`(?:[^`\\]|\\.)*`)\s*$/;

function walk(dir: string, out: string[] = []): string[] {
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const full = join(dir, entry.name);
    if (entry.isDirectory()) walk(full, out);
    else if (/\.(ts|tsx)$/.test(entry.name) && !/\.test\.(ts|tsx)$/.test(entry.name)) out.push(full);
  }
  return out;
}

// ---------------------------------------------------------------------------
// Frontend side
// ---------------------------------------------------------------------------

// NOTE: `(?<!\.)` keeps member calls like `AttachmentsApi.download(...)` out: their URL is
// built inside the callee, which this same scan covers where it calls `api.*(`.
const API_CALL =
  /(?<!\.)\bapi\s*\.\s*(get|post|put|patch|delete|head|options)(<[^;()]*>)?\(\s*("(?:[^"\\]|\\.)*"|'(?:[^'\\]|\\.)*'|`(?:[^`\\]|\\.)*`|[^"'`\s][^\n,)]*)/g;
const DOWNLOAD_CALL =
  /(?<!\.)\b(downloadFile|downloadBlob|download|both)\(\s*("(?:[^"\\]|\\.)*"|'(?:[^'\\]|\\.)*'|`(?:[^`\\]|\\.)*`|[^"'`\s][^\n,)]*)/g;
const IMPORT_PANEL = /<ImportPanel\b([\s\S]*?)\/>/g;
// Bare `api(...)` (no .method): only the interceptor replay may do this.
const BARE_API = /(?<!\.)\bapi\s*\(/g;

/** Dynamic call sites that are verified another way - each entry MUST still match. */
const DYNAMIC_ALLOWLIST: { files: string[]; contains: string; why: string }[] = [
  {
    files: ["api/client.ts"],
    contains: "api(original)",
    why: "interceptor replay of the already-validated original request"
  },
  {
    files: ["api/client.ts", "api/exports.ts"],
    contains: "api.get(url,",
    why: "download helper implementations - every caller passes a validated literal"
  },
  {
    files: ["api/client.ts", "api/exports.ts"],
    contains: "downloadFile(url: string, file",
    why: "download helper implementations - every caller passes a validated literal"
  },
  {
    files: ["api/exports.ts"],
    contains: "downloadFile(url, fileName, params)",
    why: "download forwarder - literals validated at the download-helper and both-helper call sites"
  },
  {
    files: ["api/exports.ts"],
    contains: "download(url, `${stem}.",
    why: "`both` forwarder building the excel/pdf pair from its validated literal base URL"
  },
  {
    files: ["components/ImportPanel.tsx"],
    contains: "api.get(`${base}/template`",
    why: "covered by the base= expansion validated below"
  },
  {
    files: ["components/ImportPanel.tsx"],
    contains: ".post(url, form",
    why: "upload impl - covered by the base= expansion validated below"
  }
];

function collectFrontend(): { endpoints: Endpoint[]; dynamic: string[]; unmatchedAllow: string[] } {
  const endpoints: Endpoint[] = [];
  const dynamic: string[] = [];
  const matchedAllow = new Set<string>();

  const checkDynamic = (short: string, source: string, index: number): void => {
    const snippet = source.slice(index, index + 80).replace(/\s+/g, " ");
    const hit = DYNAMIC_ALLOWLIST.find((a) => a.files.some((f) => short.endsWith(f)) && snippet.includes(a.contains));
    if (hit) matchedAllow.add(hit.contains);
    else dynamic.push(`${short}:${lineOf(source, index)}: dynamic URL ${snippet}`);
  };

  const pushLiteral = (method: string, raw: string, file: string, index: number, source: string): void => {
    const quote = raw[0];
    const body = raw.slice(1, raw.length - 1);
    const unescaped = quote === "`" ? materializeTemplate(body) : body;
    if (!unescaped.startsWith("/")) {
      checkDynamic(file, source, index);
      return;
    }
    endpoints.push({
      method: method.toUpperCase(),
      segments: splitSegments(`/api${unescaped}`),
      origin: `${file}:${lineOf(source, index)} ${method.toUpperCase()} ${unescaped.split("?")[0]}`
    });
  };

  const pushFirstArg = (method: string, firstArg: string, shortName: string, index: number, src: string): void => {
    if (/^["'`]/.test(firstArg)) {
      pushLiteral(method, firstArg, shortName, index, src);
      return;
    }
    const t = firstArg.match(TERNARY_URLS);
    if (t) {
      pushLiteral(method, t[1], shortName, index, src);
      pushLiteral(method, t[2], shortName, index, src);
    } else {
      checkDynamic(shortName, src, index);
    }
  };

  for (const file of walk(WEB_SRC)) {
    const short = file.split("src/").pop() ?? file;
    const source = readFileSync(file, "utf8");

    API_CALL.lastIndex = 0;
    let m: RegExpExecArray | null;
    while ((m = API_CALL.exec(source)) !== null) {
      pushFirstArg(m[1], m[3], short, m.index, source);
    }

    DOWNLOAD_CALL.lastIndex = 0;
    while ((m = DOWNLOAD_CALL.exec(source)) !== null) {
      pushFirstArg("get", m[2], short, m.index, source);
    }

    BARE_API.lastIndex = 0;
    while ((m = BARE_API.exec(source)) !== null) checkDynamic(short, source, m.index);

    // ImportPanel usages (the base props): expand with the component's real URL shapes.
    IMPORT_PANEL.lastIndex = 0;
    let pm: RegExpExecArray | null;
    while ((pm = IMPORT_PANEL.exec(source)) !== null) {
      const props = pm[1];
      const base = props.match(/\bbase="([^"]+)"/)?.[1];
      if (!base) {
        dynamic.push(`${short}:${lineOf(source, pm.index)}: ImportPanel with non-literal base`);
        continue;
      }
      const allowUpdate = !/supportsUpdate=\{false\}/.test(props);
      const usageLine = lineOf(source, pm.index);
      const at = (method: string, suffix: string): void => {
        endpoints.push({
          method,
          segments: splitSegments(`/api${base}${suffix}`),
          origin: `${short}:${usageLine} <ImportPanel base="${base}"> ${method} ${suffix}`
        });
      };
      at("GET", "/template");
      at("POST", "/preview");
      at("POST", "/execute");
      if (allowUpdate) {
        at("POST", "/preview-update");
        at("POST", "/execute-update");
      }
    }
  }

  const unmatchedAllow = DYNAMIC_ALLOWLIST.filter((a) => !matchedAllow.has(a.contains)).map(
    (a) => `${a.files.join(", ")} :: ${a.contains} (${a.why})`
  );
  return { endpoints, dynamic, unmatchedAllow };
}

// ---------------------------------------------------------------------------
// Backend side
// ---------------------------------------------------------------------------

function collectBackend(): Endpoint[] {
  const endpoints: Endpoint[] = [];
  for (const file of readdirSync(CONTROLLERS_DIR).filter((f) => f.endsWith(".cs"))) {
    const source = readFileSync(join(CONTROLLERS_DIR, file), "utf8");
    const lines = source.split("\n");
    let pendingRoute: { template: string; line: number } | null = null;
    let current: { controller: string; prefix: string } | null = null;

    lines.forEach((line, i) => {
      const n = i + 1;
      const route = line.match(/\[Route\("([^"]+)"\)\]/);
      if (route) {
        // A new [Route] always starts a new controller block (several files hold more
        // than one controller). A true action-level [Route] would leave the following
        // action outside any controller and fail loudly below - extend the parser then.
        if (pendingRoute) throw new Error(`${file}:${n}: stacked [Route] - extend the parser`);
        pendingRoute = { template: route[1], line: n };
        current = null;
        return;
      }
      const cls = line.match(/\bclass\s+(\w+)/);
      if (cls && cls[1].endsWith("Controller")) {
        if (!pendingRoute) throw new Error(`${file}:${n}: ${cls[1]} has no [Route] - extend the parser`);
        const short = cls[1].slice(0, -"Controller".length);
        current = {
          controller: cls[1],
          prefix: pendingRoute.template.replace(/\[controller\]/g, short)
        };
        pendingRoute = null;
        return;
      }
      if (/\[AcceptVerbs\(/.test(line)) throw new Error(`${file}:${n}: [AcceptVerbs] - extend the parser`);
      const http = line.match(/\[Http(Get|Post|Put|Patch|Delete|Head|Options)(?:\("([^"]*)"(?:,[^)]*)?\))?\]/);
      if (http) {
        if (!current) throw new Error(`${file}:${n}: action outside a controller - extend the parser`);
        const template = http[2] ?? "";
        if (/^[~/]/.test(template)) throw new Error(`${file}:${n}: absolute template "${template}" - extend the parser`);
        endpoints.push({
          method: http[1].toUpperCase(),
          segments: splitSegments(`${current.prefix}/${template}`),
          origin: `${file}:${n} ${current.controller}`
        });
      }
    });
  }
  return endpoints;
}

// ---------------------------------------------------------------------------
// Comparison
// ---------------------------------------------------------------------------

function matches(front: Endpoint, back: Endpoint): boolean {
  if (front.method !== back.method || front.segments.length !== back.segments.length) return false;
  return front.segments.every((s, i) => (s === "{p}" ? back.segments[i] === "{p}" : s === back.segments[i]));
}

describe("frontend <-> backend route contract", () => {
  it("every client URL resolves to a controller action", () => {
    const { endpoints, dynamic, unmatchedAllow } = collectFrontend();
    expect(
      dynamic,
      `Unresolvable dynamic call sites - register them in DYNAMIC_ALLOWLIST or make the URL static:\n${dynamic.join("\n")}`
    ).toEqual([]);
    expect(
      unmatchedAllow,
      `Stale DYNAMIC_ALLOWLIST entries (no longer match any call site - remove them):\n${unmatchedAllow.join("\n")}`
    ).toEqual([]);

    const backend = collectBackend();
    expect(backend.length).toBeGreaterThan(0);
    // Guard against a silently-broken scanner: the client holds hundreds of call sites,
    // so a near-empty collection means the extraction regexes stopped matching.
    expect(endpoints.length).toBeGreaterThan(200);
    const missing = endpoints.filter((f) => !backend.some((b) => matches(f, b)));
    expect(
      missing.map((m) => m.origin),
      `Client URLs with no matching controller action (guaranteed HTTP 404):\n${missing.map((m) => m.origin).join("\n")}`
    ).toEqual([]);
  });
});
