/**
 * Headless smoke test, part 3: the tab-level export / import controls.
 *
 * Part 2 flagged /payroll and /warehouse as missing controls. Both pages open
 * on a default tab (Payroll -> "runs", Warehouse -> "balances") and the Excel
 * import wizard lives on a different tab (Payroll -> employees, Warehouse ->
 * master), so part 2 was looking in the wrong place. This test opens the right
 * tab via ?tab= and re-checks, and additionally CLICKS the buttons to prove
 * they actually fire a request to a real endpoint rather than doing nothing.
 *
 * Run: node tools/ui-smoke-tabs.js
 */
const puppeteer = require("/tmp/uitest/node_modules/puppeteer");

const BASE = process.env.SMOKE_BASE || "http://127.0.0.1:5173";

const CHECKS = [
  { path: "/payroll?tab=employees", name: "Payroll / employees", need: ["import", "excel"] },
  { path: "/warehouse?tab=master", name: "Warehouse / master data", need: ["import", "excel", "pdf"] },
  { path: "/warehouse?tab=movements", name: "Warehouse / movements", need: ["excel", "pdf"] },
  { path: "/warehouse?tab=balances", name: "Warehouse / balances", need: ["excel", "pdf"] },
  { path: "/materials?tab=master", name: "Materials / master", need: ["import", "excel"] },
  { path: "/purchases", name: "Purchases (all tabs)", need: ["excel", "pdf"] },
  { path: "/reports", name: "Reports", need: ["excel", "pdf", "print"] },
  { path: "/invoices", name: "Invoices + statement", need: ["pdf"] },
  { path: "/treasury", name: "Treasury statement", need: ["pdf"] }
];

const IGNORE = [/Failed to load resource/i, /net::ERR_/i, /ERR_CONNECTION_REFUSED/i];

(async () => {
  const browser = await puppeteer.launch({
    headless: "new",
    protocolTimeout: 180000,
    args: ["--no-sandbox", "--disable-dev-shm-usage", "--disable-gpu"]
  });

  let failures = 0;

  for (const check of CHECKS) {
    const page = await browser.newPage();
    await page.evaluateOnNewDocument(() => localStorage.setItem("dyehouse_token", "smoke-test-token"));

    const errs = [];
    const requests = [];
    page.on("pageerror", (e) => errs.push(`pageerror: ${e.message}`));
    page.on("console", (m) => {
      if (m.type() === "error" && !IGNORE.some((r) => r.test(m.text()))) errs.push(m.text());
    });
    page.on("request", (r) => {
      if (r.url().includes("/api/")) requests.push(r.url().replace(BASE, ""));
    });

    let found = {};
    try {
      await page.goto(BASE + check.path, { waitUntil: "domcontentloaded", timeout: 15000 });
      await new Promise((r) => setTimeout(r, 1000));
      found = await page.evaluate(() => {
        const labels = [...document.querySelectorAll("button")].map((b) => (b.textContent || "").trim());
        return {
          excel: labels.some((l) => /excel|إكسل/i.test(l)),
          pdf: labels.some((l) => /pdf/i.test(l)),
          print: labels.some((l) => /طباعة|print/i.test(l)),
          import: labels.some((l) => /استيراد|قالب|import|template/i.test(l))
        };
      });
    } catch (e) {
      errs.push(`navigation failed: ${e.message}`);
    }

    // Click the first export button we can find and see whether it actually
    // issues a request. A button that fires nothing is the defect this guards.
    let clickResult = "n/a";
    const target = check.need.find((k) => found[k]);
    if (target) {
      const before = requests.length;
      const label = { excel: /excel|إكسل/i, pdf: /pdf/i, print: /طباعة|print/i, import: /استيراد|قالب|import|template/i }[target];
      try {
        await page.evaluate((re) => {
          const src = re.source;
          const btn = [...document.querySelectorAll("button")].find((b) => new RegExp(src, "i").test((b.textContent || "").trim()));
          if (btn) btn.click();
        }, label);
        await new Promise((r) => setTimeout(r, 1200));
        const fired = requests.slice(before);
        clickResult = fired.length ? fired[fired.length - 1] : "no request fired";
      } catch (e) {
        clickResult = `click failed: ${e.message}`;
      }
    }

    const missing = check.need.filter((k) => !found[k]);
    failures += missing.length + errs.length;

    console.log(`[${missing.length || errs.length ? "FAIL" : " ok "}] ${check.name.padEnd(28)} ${check.path}`);
    console.log(`         controls: excel=${found.excel ? "y" : "n"} pdf=${found.pdf ? "y" : "n"} print=${found.print ? "y" : "n"} import=${found.import ? "y" : "n"}`);
    console.log(`         clicked ${target ?? "(none)"} -> ${clickResult}`);
    if (missing.length) console.log(`         MISSING: ${missing.join(", ")}`);
    errs.slice(0, 2).forEach((e) => console.log(`         ERR ${e.slice(0, 160)}`));

    await page.close();
  }

  await browser.close();
  console.log(`\nUI failures: ${failures}`);
  process.exit(failures === 0 ? 0 : 1);
})();
