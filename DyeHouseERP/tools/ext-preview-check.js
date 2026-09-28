/**
 * Loads the EXTERNAL preview hostname in a real browser and asserts that Vite
 * serves the application rather than its "Blocked request" host-check page.
 *
 * Vite compares the request's Host header against `server.allowedHosts`, and
 * the hosted preview arrives under a generated hostname
 * (`<port>-<workspace>.e2b.app`) rather than localhost - so this is the only
 * test that proves the preview is actually reachable for a user. Hitting
 * 127.0.0.1 would pass even with the check misconfigured.
 *
 * Usage: node tools/ext-preview-check.js <full-preview-url>
 * Puppeteer is expected outside the project, at /tmp/uitest.
 */
const puppeteer = require("/tmp/uitest/node_modules/puppeteer");

const URL = process.argv[2];
if (!URL) {
  console.error("usage: node ext-preview-check.js <full-preview-url>");
  process.exit(2);
}

(async () => {
  const b = await puppeteer.launch({
    headless: "new",
    protocolTimeout: 180000,
    args: ["--no-sandbox", "--disable-dev-shm-usage", "--disable-gpu"]
  });
  const p = await b.newPage();
  await p.setViewport({ width: 1440, height: 900 });

  const errs = [];
  p.on("pageerror", (e) => errs.push(e.message));

  const res = await p.goto(URL, { waitUntil: "domcontentloaded", timeout: 40000 });
  await new Promise((r) => setTimeout(r, 3000));

  const probe = await p.evaluate(() => {
    const root = document.getElementById("root");
    return {
      title: document.title,
      rootNodes: root ? root.innerHTML.length : 0,
      bodyText: (document.body.innerText || "").trim().slice(0, 200),
      dir: document.documentElement.dir,
      lang: document.documentElement.lang,
      font: getComputedStyle(document.body).fontFamily,
      bg: getComputedStyle(document.body).backgroundColor,
      hasViteClient: !!document.querySelector('script[src="/@vite/client"]'),
      blocked: /Blocked request|is not allowed/i.test(document.body.innerText || "")
    };
  });

  console.log("URL           :", URL);
  console.log("HTTP status   :", res.status());
  console.log("title         :", probe.title);
  console.log("BLOCKED page? :", probe.blocked);
  console.log("root nodes    :", probe.rootNodes);
  console.log("dir / lang    :", probe.dir, "/", probe.lang);
  console.log("font-family   :", probe.font);
  console.log("body bg       :", probe.bg);
  console.log("vite client   :", probe.hasViteClient);
  console.log("page errors   :", errs.length ? errs : "none");
  console.log("visible text  :", JSON.stringify(probe.bodyText));

  await p.screenshot({ path: "/tmp/ext-preview.png" });
  await b.close();

  const ok = res.status() === 200 && !probe.blocked && probe.rootNodes > 0 && errs.length === 0;
  console.log("\nRESULT:", ok ? "PASS - external Preview URL serves the application" : "FAIL");
  process.exit(ok ? 0 : 1);
})();
