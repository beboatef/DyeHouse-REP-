/**
 * Headless-browser smoke test against the running preview.
 *
 * Puppeteer lives in /tmp (see the header note in the runbook), not in the
 * project, so package.json is untouched:
 *     mkdir -p /tmp/uitest && cd /tmp/uitest && npm i puppeteer@23
 *     node tools/ui-smoke.js
 *
 * The backend API is NOT running in this environment (no SQL Server), so every
 * /api call legitimately fails. The point of this test is to catch defects in
 * the *frontend* that a compiler cannot see: a crash on mount, an invalid hook
 * call, a missing runtime dependency, a white screen, or a route that resolves
 * to nothing. Failed /api requests are counted and reported separately, never
 * treated as UI failures.
 */
const puppeteer = require("/tmp/uitest/node_modules/puppeteer");

const BASE = process.env.SMOKE_BASE || "http://127.0.0.1:5173";

/** Routes that must render something, keyed by the app's own paths. */
const ROUTES = [
  { path: "/login", name: "Login" },
  { path: "/", name: "Dashboard (redirects to /login when signed out)" },
  { path: "/customers", name: "Customers" },
  { path: "/items", name: "Items" },
  { path: "/materials", name: "Materials / Chemicals" },
  { path: "/suppliers", name: "Suppliers" },
  { path: "/warehouse", name: "Warehouse" },
  { path: "/raw-messages", name: "Raw Material Messages" },
  { path: "/production-orders", name: "Job Orders" },
  { path: "/ready-goods", name: "Ready Goods" },
  { path: "/deliveries", name: "Deliveries" },
  { path: "/invoices", name: "Invoices + Customer Statement" },
  { path: "/treasury", name: "Treasury" },
  { path: "/checks", name: "Checks" },
  { path: "/formation-requests", name: "Formation Requests" },
  { path: "/external-releases", name: "External Processing" },
  { path: "/customer-transfers", name: "Customer Transfers" },
  { path: "/stock-adjustments", name: "Stock Adjustments" },
  { path: "/material-sales", name: "Material Sales" },
  { path: "/supplies", name: "Operating Supplies" },
  { path: "/purchases", name: "Purchases" },
  { path: "/payroll", name: "Payroll" },
  { path: "/reports", name: "Reports" },
  { path: "/users", name: "Users / Permissions" },
  { path: "/settings", name: "Settings" },
  { path: "/period-closing", name: "Period Closing" },
  { path: "/audit-log", name: "Audit Log" }
];

/** Console noise that is not a defect. */
const IGNORE = [
  /Failed to load resource/i,
  /net::ERR_/i,
  /ERR_CONNECTION_REFUSED/i,
  /You should provide a value for the/i, // a deliberate "missing value" demo
  /React Router Future Flag/i
];

const isFailure = (type, text) =>
  !IGNORE.some((re) => re.test(text)) &&
  (type === "error" || /error|exception|invalid hook|rendered more hooks/i.test(text));

(async () => {
  const browser = await puppeteer.launch({
    headless: "new",
    protocolTimeout: 180000,
    args: [
      "--no-sandbox",
      "--disable-dev-shm-usage",
      "--disable-gpu",
      // The API host is down; without this every route stalls on a dead
      // connection and the run takes minutes instead of seconds.
      "--host-resolver-rules=MAP localhost 127.0.0.1, EXCLUDE 127.0.0.1"
    ]
  });

  let totalFailures = 0;
  let totalApiFailures = 0;
  const summary = [];

  for (const route of ROUTES) {
    const page = await browser.newPage();
    const errors = [];
    let apiFailures = 0;

    page.on("console", (msg) => {
      const text = msg.text();
      if (/api\//.test(text) && /failed|error/i.test(text)) apiFailures++;
      if (isFailure(msg.type(), text)) errors.push(`console.${msg.type()}: ${text}`);
    });
    page.on("pageerror", (err) => errors.push(`pageerror: ${err.message}`));
    page.on("requestfailed", (req) => {
      const url = req.url();
      if (url.includes("/api/")) apiFailures++;
      else errors.push(`requestfailed: ${url} (${req.failure()?.errorText})`);
    });

    let rendered = false;
    let height = 0;
    let finalPath = route.path;

    try {
      // The API is down, so `networkidle`/`networkidle2` would block until its
      // 30s socket timeout on every route. `domcontentloaded` plus a short
      // settle window is what actually matters here: does React mount and paint.
      const response = await page.goto(BASE + route.path, {
        waitUntil: "domcontentloaded",
        timeout: 15000
      });

      await new Promise((r) => setTimeout(r, 500));

      const probe = await page.evaluate(() => {
        const root = document.getElementById("root");
        return {
          htmlLength: root ? root.innerHTML.length : 0,
          text: (document.body.innerText || "").trim().slice(0, 120),
          dir: document.documentElement.dir,
          lang: document.documentElement.lang,
          // Is any stylesheet actually applied? (catches unstyled/white-screen.)
          styled: getComputedStyle(document.body).fontFamily,
          bg: getComputedStyle(document.body).backgroundColor
        };
      });

      finalPath = new URL(page.url()).pathname;
      rendered = probe.htmlLength > 0;
      height = probe.htmlLength;
      summary.push({ ...route, ...probe, status: response ? response.status() : 0 });

      if (!rendered) errors.push("root element is empty - the app rendered nothing");
      if (probe.bg === "rgba(0, 0, 0, 0)" || probe.bg === "transparent") {
        errors.push(`body has no background - Tailwind base styles look unloaded (${probe.bg})`);
      }
      if (!probe.dir) errors.push("document has no dir attribute - RTL/LTR is not applied");
    } catch (e) {
      errors.push(`navigation failed: ${e.message}`);
      summary.push({ ...route, error: e.message });
    }

    totalFailures += errors.length;
    totalApiFailures += apiFailures;

    const status = errors.length ? "FAIL" : " ok ";
    console.log(
      `[${status}] ${route.name.padEnd(46)} ${String(route.path).padEnd(22)}` +
      ` -> ${finalPath.padEnd(20)} nodes=${String(height).padStart(6)} api-errs=${apiFailures}`
    );
    errors.forEach((e) => console.log(`         ${e.slice(0, 220)}`));

    await page.close();
  }

  // --- the shell: fonts, Tailwind, RTL, language switching -------------
  console.log("\n== shell checks ==");
  const shell = await browser.newPage();
  const shellErrors = [];
  shell.on("pageerror", (e) => shellErrors.push(e.message));
  shell.on("console", (m) => { if (m.type() === "error" && !IGNORE.some((r) => r.test(m.text()))) shellErrors.push(m.text()); });

  await shell.goto(BASE + "/login", { waitUntil: "domcontentloaded", timeout: 15000 });
  await new Promise((r) => setTimeout(r, 800));

  const shellProbe = await shell.evaluate(() => {
    const body = getComputedStyle(document.body);
    return {
      dir: document.documentElement.dir,
      lang: document.documentElement.lang,
      fontFamily: body.fontFamily,
      bg: body.backgroundColor,
      hasTailwind: !!document.querySelector("main, .min-h-screen, [class*='bg-']"),
      // Did the Cairo webfont actually get requested and applied?
      cairoLoaded: document.fonts ? [...document.fonts].some((f) => f.family.includes("Cairo")) : null
    };
  });

  console.log(`  dir=${shellProbe.dir}  lang=${shellProbe.lang}`);
  console.log(`  font-family=${shellProbe.fontFamily}`);
  console.log(`  body background=${shellProbe.bg}`);
  console.log(`  tailwind shell present=${shellProbe.hasTailwind}`);
  console.log(`  Cairo webfont registered=${shellProbe.cairoLoaded}`);
  if (shellProbe.dir !== "rtl") shellErrors.push(`expected rtl default, got ${shellProbe.dir}`);
  if (!/Cairo/.test(shellProbe.fontFamily)) shellErrors.push("Cairo is not in the applied font stack");

  // Language switch -> LTR
  let ltrOk = "no switch control found";
  const switched = await shell.evaluate(() => {
    const btn = [...document.querySelectorAll("button")].find((b) => /english|EN|ENGLISH/i.test(b.textContent || ""));
    if (!btn) return false;
    btn.click();
    return true;
  });
  if (switched) {
    await new Promise((r) => setTimeout(r, 400));
    const after = await shell.evaluate(() => ({
      dir: document.documentElement.dir,
      lang: document.documentElement.lang
    }));
    ltrOk = `after switch: dir=${after.dir} lang=${after.lang}`;
    if (after.dir !== "ltr") shellErrors.push(`language switch did not flip to ltr (got ${after.dir})`);
  }
  console.log(`  language switch: ${ltrOk}`);

  shellErrors.forEach((e) => console.log(`  SHELL FAIL ${e.slice(0, 200)}`));
  totalFailures += shellErrors.length;

  await browser.close();

  console.log("\n---------------------------------------------");
  console.log(`routes tested        : ${ROUTES.length}`);
  console.log(`UI failures          : ${totalFailures}`);
  console.log(`API failures (no DB) : ${totalApiFailures}  <- expected, backend is not running`);
  process.exit(totalFailures === 0 ? 0 : 1);
})();
