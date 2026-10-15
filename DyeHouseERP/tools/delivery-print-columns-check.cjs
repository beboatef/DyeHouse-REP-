/**
 * Browser check for the printable DELIVERY PERMIT's quantity columns (spec sections 31 and 39).
 *
 * Why this exists as a browser test: the column choice is UI state, so a server test can only cover
 * the PDF half (see tests/DyeHouseERP.UnitTests/DeliveryPermitPdfColumnsTests.cs). This one drives the
 * real React page instead and checks what an operator actually sees on paper:
 *   1-4. KG only / Meter only / rolls only / all three - the table shows exactly the chosen columns;
 *   5.   the request the PDF download makes carries the SAME three flags, so the file matches the screen;
 *   6.   the permit's identity always prints, a hidden column is absent (never zero-filled), and hiding
 *        something issues no write to the API;
 *   7.   the sub-customer permit's format is stored separately from the ordinary permit's.
 *
 * There is no SQL Server in this environment, so the API is stubbed with request interception (the same
 * approach as tools/printing-ui-check.cjs): this test exercises the FRONTEND, and the server-side
 * projection is proven separately by the unit suite.
 *
 * Puppeteer lives in /tmp, not in the project (so package.json is untouched):
 *     mkdir -p /tmp/uitest && cd /tmp/uitest && npm i puppeteer
 *     cd src/DyeHouseERP.Web && npm run dev          # vite dev server (binds [::1]:5173)
 *     node tools/delivery-print-columns-check.cjs
 *
 * Two environment details this script depends on, both learned the hard way:
 *   * it launches `headless: "shell"` (chrome-headless-shell). Full Chrome needs a thread count this
 *     sandbox does not grant (pthread_create -> EAGAIN), and it dies before the first page loads;
 *     the headless shell starts fine.
 *   * Chrome's shared libraries may have to be supplied from a local extraction when the image has no
 *     root and no browser packages, e.g.
 *         apt-get install --print-uris -y <pkgs> | ...  # download the .debs
 *         dpkg-deb -x <pkg>.deb /tmp/chromedeps
 *         export LD_LIBRARY_PATH=/tmp/chromedeps/usr/lib/x86_64-linux-gnu
 */
const puppeteer = require("/tmp/uitest/node_modules/puppeteer");

// `localhost` on purpose: vite's default dev server binds the IPv6 loopback ([::1]:5173), so a literal
// 127.0.0.1 would be refused. The browser resolves the name itself and reaches whichever is listening.
const BASE = process.env.SMOKE_BASE || "http://localhost:5173";
const PERMIT_ID = "11111111-1111-4111-8111-111111111111";
const PRIMARY_ID = "22222222-2222-4222-8222-222222222222";

/** A sub-customer permit holding one line whose KG, Meter and roll figures are all distinct. */
const permit = {
  id: PERMIT_ID,
  deliveryNumber: "SUB-2026-000041",
  deliveryDate: "2026-10-14T00:00:00Z",
  customerId: "33333333-3333-4333-8333-333333333333",
  customerCode: "C-S",
  customerName: "Sub Buyer",
  status: "Prepared",
  notes: null,
  isSubCustomerPermit: true,
  primaryDeliveryId: PRIMARY_ID,
  primaryDeliveryNumber: "DEL-2026-000125",
  primaryCustomerId: "44444444-4444-4444-8444-444444444444",
  primaryCustomerCode: "C-P",
  primaryCustomerName: "Primary Buyer",
  lines: [
    {
      id: "55555555-5555-4555-8555-555555555555",
      productionOrderId: "66666666-6666-4666-8666-666666666666",
      productionOrderNumber: "PRD-P-1",
      productionOrderDate: "2026-10-01T00:00:00Z",
      itemId: "77777777-7777-4777-8777-777777777777",
      itemCode: "IT-1",
      itemName: "Fabric",
      color: "Red",
      quantityKg: 120,
      quantityMeter: 400,
      pieceCount: 7,
      rawOrigin: null,
      drawingNumbers: ["DRW-9"],
      originalRawQuantityKg: 90,
      originalRawQuantityMeter: null,
      originalOrderRawQuantityKg: 600,
      originalOrderRawQuantityMeter: null,
      remainingOriginalRawKg: 510,
      remainingOriginalRawMeter: 0
    }
  ]
};

const failures = [];
const check = (ok, label, detail) => {
  if (ok) console.log(`  ok    ${label}`);
  else {
    failures.push(label);
    console.log(`  FAIL  ${label}${detail ? ` -> ${detail}` : ""}`);
  }
};

/** Reads the printable table: its headers, and each body row's cells. */
const readTable = () =>
  page.evaluate(() => {
    const table = Array.from(document.querySelectorAll("table")).find((t) => t.querySelector("thead"));
    if (!table) return null;
    return {
      headers: Array.from(table.querySelectorAll("thead th")).map((th) => th.textContent.trim()),
      rows: Array.from(table.querySelectorAll("tbody tr")).map((tr) =>
        Array.from(tr.querySelectorAll("td")).map((td) => td.textContent.trim())
      )
    };
  });

const readChoice = () =>
  page.evaluate(() => ({
    sub: JSON.parse(localStorage.getItem("dyehouse_delivery_print_columns.sub") || "null"),
    ordinary: JSON.parse(localStorage.getItem("dyehouse_delivery_print_columns") || "null")
  }));

let page;

(async () => {
  const browser = await puppeteer.launch({
    headless: "shell",
    protocolTimeout: 180000,
    args: [
      "--no-sandbox", "--disable-setuid-sandbox", "--disable-dev-shm-usage", "--disable-gpu",
      "--no-first-run", "--disable-background-networking", "--disable-extensions", "--mute-audio",
      "--renderer-process-limit=1"
    ]
  });

  try {
    page = await browser.newPage();
    await page.setViewport({ width: 1400, height: 1000 });

    const consoleErrors = [];
    const writes = [];
    const pdfRequests = [];

    page.on("pageerror", (e) => consoleErrors.push(e.message));
    page.on("console", (m) => { if (m.type() === "error") consoleErrors.push(m.text()); });

    await page.evaluateOnNewDocument(() => {
      localStorage.setItem("dyehouse_token", "ui-only-stub");
      localStorage.setItem("dyehouse_user", JSON.stringify({ displayName: "Tester", roles: "admin" }));
      localStorage.setItem("dyehouse_lang", "en");
    });

    await page.setRequestInterception(true);
    page.on("request", (request) => {
      const url = request.url();
      const method = request.method();

      // Path prefix, not a substring: `/src/api/client.ts` is the app's OWN dev module (vite serves it
      // from the dev server), and stubbing it with JSON breaks the bundle before React ever mounts.
      let pathName = "";
      try {
        pathName = new URL(url).pathname;
      } catch {
        pathName = url;
      }
      if (!pathName.startsWith("/api/")) return request.continue();
      if (method !== "GET" && method !== "HEAD") writes.push(`${method} ${url}`);

      if (/\/api\/deliveries\/[^/]+\/pdf/.test(url)) {
        pdfRequests.push(url);
        return request.respond({ status: 200, contentType: "application/pdf", body: Buffer.from("%PDF-1.4 stub") });
      }
      if (url.includes("/api/deliveries/") && method === "GET") {
        return request.respond({ status: 200, contentType: "application/json", body: JSON.stringify(permit) });
      }
      if (/\/api\/deliveries(\?|$)/.test(url)) {
        return request.respond({ status: 200, contentType: "application/json", body: JSON.stringify([permit]) });
      }
      if (url.includes("/api/customers")) {
        return request.respond({ status: 200, contentType: "application/json", body: "[]" });
      }
      if (url.includes("/api/settings")) {
        return request.respond({
          status: 200,
          contentType: "application/json",
          body: JSON.stringify({ companyNameAr: "شركة تجريبية", companyNameEn: "Test Co", logoDataUrl: null })
        });
      }
      return request.respond({ status: 200, contentType: "application/json", body: "[]" });
    });

    console.log("\nPRINT PREVIEW - which quantity columns reach the paper");

    await page.goto(`${BASE}/print/delivery/${PERMIT_ID}`, { waitUntil: "networkidle2" });
    await page.waitForFunction(() => !!document.querySelector("thead th"), { timeout: 20000 });

    // Establish the all-on baseline and take the identity columns from it: the permit's own data
    // (drawing number, job order, item) is never part of the chooser, whatever language renders it in.
    const baseline = await readTable();
    const identityCount = baseline.headers.length - 3;
    const identity = baseline.headers.slice(0, identityCount);
    check(identityCount >= 3, "identity columns print (drawing number, job order, item)", JSON.stringify(identity));
    check(
      baseline.headers.slice(identityCount).length === 3,
      "all three quantity columns print by default",
      JSON.stringify(baseline.headers)
    );

    /** All eight combinations: the three quantity columns' presence must follow the checkboxes exactly. */
    const combinations = [
      { kg: true, meter: false, rolls: false },
      { kg: false, meter: true, rolls: false },
      { kg: false, meter: false, rolls: true },
      { kg: true, meter: true, rolls: true },
      { kg: false, meter: true, rolls: true },
      { kg: true, meter: false, rolls: true },
      { kg: true, meter: true, rolls: false },
      { kg: false, meter: false, rolls: false }
    ];

    for (const want of combinations) {
      // Drive the real checkboxes rather than writing storage, so the toggle path is what is tested.
      const boxes = await page.$$(".no-print input[type=checkbox]");
      const keys = ["kg", "meter", "rolls"];
      for (let i = 0; i < keys.length; i++) {
        const isOn = await page.evaluate((el) => el.checked, boxes[i]);
        if (isOn !== want[keys[i]]) await boxes[i].click();
      }
      await new Promise((r) => setTimeout(r, 120));

      const table = await readTable();
      const label = `kg=${want.kg} meter=${want.meter} rolls=${want.rolls}`;
      const expectedHeaders = [
        ...identity,
        ...(want.kg ? [baseline.headers[identityCount]] : []),
        ...(want.meter ? [baseline.headers[identityCount + 1]] : []),
        ...(want.rolls ? [baseline.headers[identityCount + 2]] : [])
      ];

      check(
        JSON.stringify(table.headers) === JSON.stringify(expectedHeaders),
        `${label}: the printed columns are exactly the chosen ones`,
        `got ${JSON.stringify(table.headers)}`
      );
      check(
        table.rows.every((r) => r.length === table.headers.length),
        `${label}: every row still has one cell per column`,
        JSON.stringify(table.rows)
      );

      // A hidden column is ABSENT, never zero-filled, and a visible one shows the recorded figure.
      const cells = table.rows[0] ?? [];
      if (want.kg) check(cells.includes("120"), `${label}: the recorded KG still prints`);
      else check(!cells.includes("120") && !cells.includes("0"), `${label}: KG is absent, not zeroed`, JSON.stringify(cells));
      if (want.meter) check(cells.includes("400"), `${label}: the recorded Meter still prints`);
      else check(!cells.includes("400") && !cells.includes("0"), `${label}: Meter is absent, not zeroed`, JSON.stringify(cells));
      if (want.rolls) check(cells.includes("7"), `${label}: the recorded roll count still prints`);
      else check(!cells.includes("7") && !cells.includes("0"), `${label}: rolls are absent, not zeroed`, JSON.stringify(cells));

      // The identity is printed in EVERY format, including the one with no quantity column at all.
      check(
        identity.every((h) => table.headers.includes(h)),
        `${label}: the permit's identity columns are never hidden`
      );
    }

    console.log("\nPERSISTENCE AND PER-DOCUMENT ISOLATION");

    const stored = await readChoice();
    check(
      stored.sub && stored.sub.kg === false && stored.sub.meter === false && stored.sub.rolls === false,
      "the last choice is remembered for the sub-customer permit",
      JSON.stringify(stored)
    );
    check(
      stored.ordinary === null,
      "the sub-customer permit's format never writes the ordinary permit's key",
      JSON.stringify(stored)
    );

    // The PDF the register downloads must be asked for with the SAME columns (point 5).
    console.log("\nPDF DOWNLOAD - the same columns are requested");
    await page.goto(`${BASE}/deliveries`, { waitUntil: "networkidle2" });
    await page.waitForFunction(
      () => Array.from(document.querySelectorAll("button")).some((b) => b.textContent.includes("PDF")),
      { timeout: 20000 }
    );
    const clicked = await page.evaluate(() => {
      const button = Array.from(document.querySelectorAll("button")).find((b) => b.textContent.trim().startsWith("تنزيل PDF"));
      if (!button) return false;
      button.click();
      return true;
    });
    check(clicked, "the register offers the permit PDF");
    await new Promise((r) => setTimeout(r, 500));

    const pdfUrl = pdfRequests[pdfRequests.length - 1] ?? "";
    const query = new URLSearchParams(pdfUrl.split("?")[1] ?? "");
    check(
      pdfRequests.length >= 1 && query.get("includeKg") === "false" && query.get("includeMeter") === "false" &&
        query.get("includeRolls") === "false",
      "the PDF is requested with the columns the user chose",
      pdfUrl
    );

    console.log("\nNO WRITES FROM PRINTING");
    check(writes.length === 0, "printing or hiding a column sent no write to the API", JSON.stringify(writes));
    check(
      consoleErrors.length === 0,
      "no page/console errors on the print or register screens",
      JSON.stringify(consoleErrors.slice(0, 3))
    );
  } catch (error) {
    failures.push(`threw: ${error.message}`);
    console.log(`\n  FAIL  the check threw: ${error.message}`);
  } finally {
    await browser.close();
  }

  console.log(`\nfailures: ${failures.length}`);
  if (failures.length) console.log(failures.map((f) => ` - ${f}`).join("\n"));
  process.exit(failures.length === 0 ? 0 : 1);
})();
