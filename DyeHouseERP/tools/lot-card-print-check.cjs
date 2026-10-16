/**
 * Browser check for BATCH PRINTING of the lot traceability card (بطاقة تتبع اللوط).
 *
 * Why this exists as a browser test: selecting several lots and printing their cards as ONE document is
 * UI behaviour (selection state, the URL it builds, one sheet per lot, the page break between sheets),
 * so a server test cannot cover it. The server half - the same card as a downloaded PDF, and the refusal
 * of a request that does not name exactly one lot - is proven separately by
 * tests/DyeHouseERP.UnitTests/LotCardPdfTests.cs and StockTraceabilityLotIdentityTests.cs.
 *
 * ONE LOT IS ONE (MESSAGE, ITEM) PAIR. A receipt message carrying two items is TWO independent balances,
 * so the checks below deliberately include two lots that share a message number: they must print as two
 * separate cards, each naming its own item, and ticking one must never tick the other.
 *
 * What it drives, on the real React screens:
 *   PART A - the batch document (always run):
 *     1-3. the document holds ONE independent card per lot, each naming its own lot and carrying only its
 *          own movements, and every card but the last starts a new sheet;
 *     4.   the batch has a single toolbar (not one per card);
 *     5.   each card's PDF button asks the server for THAT lot on the existing lot-card endpoint, naming
 *          the item as well as the message;
 *     6.   the same lot listed twice prints ONE card;
 *     7-8. two items of the SAME message print as two independent cards, each naming its own item and
 *          none of the other's numbers;
 *     9.   a lot that cannot be resolved fails in ITS OWN place - the other cards still print;
 *     10.  a link that names only the message (two items behind it) prints NO card and says why, rather
 *          than printing whichever item came first;
 *     11.  an empty selection prints a clear message and NO card at all;
 *     12.  KG and Meter stay independent: a lot whose ledger never recorded a meter prints no meter;
 *     13.  printing writes nothing - no non-GET request reaches the API.
 *   PART B - the balances screen (SKIPPED when the environment cannot render it, see below):
 *     14-16. a checkbox per lot, the print action disabled until something is ticked and counting the
 *            selection, and pressing it opening the document for exactly the ticked lots - including two
 *            items of one message, which travel as two distinct tokens.
 *
 * There is no SQL Server in this environment, so the API is stubbed with request interception (the same
 * approach as tools/printing-ui-check.cjs and tools/delivery-print-columns-check.cjs): this test
 * exercises the FRONTEND, and the server-side document is proven by the unit suite.
 *
 * If the browser dies on the balances screen instead of asserting, PART B is reported as SKIPPED (never
 * as passed), so the selection half is never counted as verified when it was not - see the fontconfig
 * note below, which is the usual cause.
 *
 * Puppeteer lives in /tmp, not in the project (so package.json is untouched):
 *     mkdir -p /tmp/uitest && cd /tmp/uitest && npm i puppeteer
 *     cd src/DyeHouseERP.Web && npm run build && npm run preview -- --port 4173
 *     LD_LIBRARY_PATH=/tmp/chromedeps/root/usr/lib/x86_64-linux-gnu \
 *       FONTCONFIG_FILE=/tmp/fonts/fonts.conf FONTCONFIG_PATH=/tmp/fonts \
 *       SMOKE_BASE=http://127.0.0.1:4173 node tools/lot-card-print-check.cjs
 *
 * Environment details this script depends on, all learned the hard way:
 *   * launch `headless: "shell"` (chrome-headless-shell). Full Chrome needs a thread count this sandbox
 *     does not grant (pthread_create -> EAGAIN) and dies before the first page loads;
 *   * Chrome's shared libraries come from a local extraction when the image has no root and no browser
 *     packages:  apt-get download <pkgs> && dpkg-deb -x <pkg>.deb /tmp/chromedeps/root
 *   * FONTCONFIG IS MANDATORY. With no /etc/fonts/fonts.conf the renderer aborts with
 *     "FATAL: SkFontMgr_FontConfigInterface.cpp Not implemented" the moment a screen needs font matching
 *     (the balances table), which puppeteer reports only as "Navigating frame was detached". Fix:
 *         apt-get download fonts-dejavu-core && dpkg-deb -x fonts-dejavu-core*.deb /tmp/fonts/root
 *         printf '<?xml version="1.0"?>\n<!DOCTYPE fontconfig SYSTEM "fonts.dtd">\n<fontconfig>\n  <dir>/tmp/fonts/root/usr/share/fonts</dir>\n  <cachedir>/tmp/fontcache</cachedir>\n</fontconfig>\n' > /tmp/fonts/fonts.conf
 *   * the built app is served (vite preview) rather than the dev server: the dev server reloads the page
 *     the first time it optimizes a dependency, which tears the frame down mid-assertion.
 */
const puppeteer = require("/tmp/uitest/node_modules/puppeteer");

const BASE = process.env.SMOKE_BASE || "http://127.0.0.1:4173";

const LOT_A = "aaaaaaaa-1111-4111-8111-aaaaaaaaaaaa"; // message CODE_A, item 1
const LOT_D = "dddddddd-4444-4444-8444-dddddddddddd"; // the SAME message CODE_A, item 2
const LOT_B = "bbbbbbbb-2222-4222-8222-bbbbbbbbbbbb"; // message CODE_B, item 1
const LOT_C = "cccccccc-3333-4333-8333-cccccccccccc"; // a lot the API cannot resolve

const CODE_A = "MSG-2026-000001";
const CODE_B = "MSG-2026-000002";

const ITEM_1 = "11111111-1111-4111-8111-111111111111"; // IT-1 Fabric
const ITEM_2 = "33333333-3333-4333-8333-333333333333"; // IT-2 Voile
const CUSTOMER = "22222222-2222-4222-8222-222222222222";
const WAREHOUSE = "99999999-9999-4999-8999-999999999999";

/**
 * A lot's identity as the query stamps it on the row and on every movement of it - a raw lot is
 * (message, item, customer). The card and the print URL both carry it, which is why the two items of
 * one message never collapse into one card.
 */
const lotKeyOf = (messageId, itemId) => `R|${messageId}|${itemId}|${CUSTOMER}`;

/** One lot as it travels in the print URL: the message, then "@" and the item that makes it a balance. */
const tokenOf = (messageId, itemId) => `${messageId}@${itemId}`;

/** A balance row as the traceability read returns it: one row per LOT, not per message. */
const row = (rawMessageId, messageNumber, extra = {}) => {
  const merged = {
    scope: "raw",
    itemId: ITEM_1,
    itemCode: "IT-1",
    itemName: "Fabric",
    baseUnit: "KG",
    secondaryUnit: "Meter",
    rawMessageId,
    messageNumber,
    messageDate: "2026-10-01T00:00:00Z",
    messageStatus: "Accepted",
    productionOrderId: null,
    orderNumber: null,
    orderNumbers: null,
    orderDate: null,
    orderStatus: null,
    color: "Red",
    producedPieceCount: null,
    materialId: null, materialCode: null, materialName: null, materialUnit: null, materialKind: null,
    warehouseId: WAREHOUSE,
    warehouseName: "Raw WH",
    customerId: CUSTOMER,
    customerCode: "C-1",
    customerName: "Buyer One",
    supplierName: null, purchaseOrderNumber: null,
    receivedKg: 200, receivedMeter: 600, receivedQuantity: null,
    issuedToProductionKg: 100, issuedToProductionMeter: 300, issuedToProductionQuantity: null,
    underProductionKg: 0, underProductionMeter: 0,
    returnedKg: 0, returnedMeter: 0, returnedQuantity: null,
    producedKg: null, producedMeter: null,
    deliveredKg: 0, deliveredMeter: 0, deliveredPieceCount: null,
    deliveryNumbers: null, lastDeliveryDate: null,
    remainingKg: 100, remainingMeter: 300, remainingQuantity: null,
    movementCount: 2,
    ...extra
  };
  merged.lotKey = lotKeyOf(rawMessageId, merged.itemId);
  return merged;
};

/**
 * A ledger movement. A null metre means the movement recorded no metre at all, and the movement carries
 * the lot it BELONGS to - a lot's card must only ever show its own movements' documents.
 */
const movement = (id, date, kind, direction, quantityKg, meter, balanceKg, balanceMeter,
  lot = { id: LOT_A, number: CODE_A, itemId: ITEM_1, itemCode: "IT-1", itemName: "Fabric" }) => ({
  id,
  scope: "raw",
  lotKey: lotKeyOf(lot.id, lot.itemId),
  date,
  documentType: kind === "Received" ? "RawReceiptMessage" : "ProductionOrder",
  documentNumber: kind === "Received" ? lot.number : "PRD-1",
  documentId: "44444444-4444-4444-8444-444444444444",
  kind,
  direction,
  isReversal: false,
  rawMessageId: lot.id,
  messageNumber: lot.number,
  productionOrderId: null, orderNumber: null, materialId: null, materialCode: null,
  itemId: lot.itemId, itemCode: lot.itemCode, itemName: lot.itemName,
  warehouseId: WAREHOUSE, warehouseName: "Raw WH",
  customerId: CUSTOMER, customerCode: "C-1",
  deliveryId: null, supplierName: null, purchaseOrderNumber: null,
  quantityKg, quantityMeter: meter, quantity: null, unit: null,
  balanceAfterKg: balanceKg, balanceAfterMeter: balanceMeter, balanceAfter: null,
  createdBy: "clerk",
  createdAtUtc: `${date}T08:00:00Z`
});

const trace = (rows, movements, extra = {}) => ({
  scope: "raw",
  rows,
  movements,
  generatedAtUtc: "2026-10-16T10:00:00Z",
  totalRemainingKg: rows.reduce((s, r) => s + (r.remainingKg ?? 0), 0),
  totalRemainingMeter: rows.some((r) => r.remainingMeter != null) ? rows.reduce((s, r) => s + (r.remainingMeter ?? 0), 0) : null,
  totalRemainingQuantity: null,
  totalUnderProductionKg: 0, totalUnderProductionMeter: null,
  totalReceivedKg: 200, totalReceivedMeter: 600, totalReceivedQuantity: null,
  totalIssuedKg: 0, totalIssuedMeter: null, totalIssuedQuantity: null,
  totalReturnedKg: 0, totalReturnedMeter: null, totalReturnedQuantity: null,
  totalProducedKg: null, totalProducedMeter: null, totalDeliveredKg: null, totalDeliveredMeter: null,
  ...extra
});

const lotOf = (rawMessageId) => rawMessageId === LOT_D
  ? { id: LOT_D, number: CODE_A, itemId: ITEM_2, itemCode: "IT-2", itemName: "Voile" }
  : { id: LOT_B, number: CODE_B, itemId: ITEM_1, itemCode: "IT-1", itemName: "Fabric" };

/** The second item of message A: a lot in its own right, with its own stock and its own movements. */
const itemTwoOfMessageA = () => {
  const lot = lotOf(LOT_D);
  return trace(
    [row(LOT_D, CODE_A, {
      itemId: ITEM_2, itemCode: "IT-2", itemName: "Voile", color: "Green",
      receivedKg: 40, receivedMeter: null, issuedToProductionKg: 0, issuedToProductionMeter: null,
      underProductionKg: 0, underProductionMeter: null, remainingKg: 40, remainingMeter: null,
      movementCount: 1
    })],
    [movement("m4", "2026-10-01", "Received", "In", 40, null, 40, null, lot)]
  );
};

/**
 * What the API answers for the identity it was asked about. A request that names only the message (no
 * item) gets BOTH lots of that message, which is what the print screen has to refuse instead of drawing
 * a card for whichever came first.
 */
const dataFor = (rawMessageId, itemId) => {
  // A lot is named by the message AND the item: a pair the ledger does not have resolves to nothing, the
  // same way the real query answers for an identity that matches no balance.
  const namesItem = (expected) => itemId === null || itemId === expected;
  if (rawMessageId === LOT_D && namesItem(ITEM_2)) return itemTwoOfMessageA();
  if (rawMessageId === LOT_A && namesItem(ITEM_1))
    return itemId === null
      ? trace(
        [row(LOT_A, CODE_A), row(LOT_D, CODE_A, { itemId: ITEM_2, itemCode: "IT-2", itemName: "Voile", color: "Green", receivedKg: 40, remainingKg: 40, remainingMeter: null, movementCount: 1 })],
        [
          movement("m1", "2026-10-01", "Received", "In", 200, 600, 200, 600),
          movement("m2", "2026-10-05", "Issued", "Out", 100, 300, 100, 300),
          movement("m4", "2026-10-01", "Received", "In", 40, null, 40, null, lotOf(LOT_D))
        ]
      )
      : trace(
        [row(LOT_A, CODE_A)],
        [
          movement("m1", "2026-10-01", "Received", "In", 200, 600, 200, 600),
          movement("m2", "2026-10-05", "Issued", "Out", 100, 300, 100, 300)
        ]
      );
  if (rawMessageId === LOT_B)
    return trace(
      [row(LOT_B, CODE_B, {
        remainingMeter: null, receivedMeter: null, issuedToProductionMeter: null,
        deliveredMeter: null, returnedMeter: null, underProductionMeter: null,
        color: "Blue", movementCount: 1
      })],
      [movement("m3", "2026-10-02", "Received", "In", 50, null, 50, null, lotOf(LOT_B))]
    );
  return trace([], []); // LOT_C: no such lot (or no permission)
};

/** The four lots the balances list offers: two of them belong to the SAME receipt message. */
const listRows = [
  row(LOT_A, CODE_A),
  row(LOT_D, CODE_A, { itemId: ITEM_2, itemCode: "IT-2", itemName: "Voile", color: "Green", receivedKg: 40, receivedMeter: null, remainingKg: 40, remainingMeter: null, movementCount: 1 }),
  row(LOT_B, CODE_B, { color: "Blue" }),
  row(LOT_C, "MSG-2026-000003", { color: "Green" })
];

const failures = [];
const skipped = [];

const check = (ok, label, detail) => {
  if (ok) console.log(`  ok    ${label}`);
  else {
    failures.push(label);
    console.log(`  FAIL  ${label}${detail ? ` -> ${detail}` : ""}`);
  }
};

const skip = (label, reason) => {
  skipped.push(`${label} (${reason})`);
  console.log(`  SKIP  ${label} -> ${reason}`);
};

let page;
let browser;

/** The batch document: every card, its own lot, one sheet each. */
const readSheets = () =>
  page.evaluate(() => {
    const sheets = Array.from(document.querySelectorAll(".paper"));
    const toolbarButtons = Array.from(document.querySelectorAll("button"))
      .filter((b) => b.textContent.includes("طباعة")).length;
    return {
      count: sheets.length,
      toolbarButtons,
      breakAfter: sheets.map((s) => getComputedStyle(s).breakAfter || getComputedStyle(s).pageBreakAfter),
      text: sheets.map((s) => s.textContent)
    };
  });

async function launch() {
  browser = await puppeteer.launch({
    headless: "shell",
    protocolTimeout: 180000,
    args: [
      "--no-sandbox", "--disable-setuid-sandbox", "--disable-dev-shm-usage", "--disable-gpu",
      "--no-first-run", "--disable-background-networking", "--disable-extensions", "--mute-audio",
      "--renderer-process-limit=1"
    ]
  });

  page = await browser.newPage();
  await page.setViewport({ width: 1400, height: 1000 });
  await page.evaluateOnNewDocument(() => {
    localStorage.setItem("dyehouse_token", "ui-only-stub");
    localStorage.setItem("dyehouse_user", JSON.stringify({ displayName: "Tester", roles: "admin" }));
    localStorage.setItem("dyehouse_lang", "en");
  });
  await page.setRequestInterception(true);
}

/** Puts a fresh page under the same stubs, for a browser instance whose previous page died. */
async function freshPage() {
  await page.close().catch(() => {});
  page = await browser.newPage();
  await page.setViewport({ width: 1400, height: 1000 });
  await page.evaluateOnNewDocument(() => {
    localStorage.setItem("dyehouse_token", "ui-only-stub");
    localStorage.setItem("dyehouse_user", JSON.stringify({ displayName: "Tester", roles: "admin" }));
    localStorage.setItem("dyehouse_lang", "en");
  });
  await page.setRequestInterception(true);
  bindStubs();
}

const consoleErrors = [];
const writes = [];
const cardRequests = [];

function bindStubs() {
  page.on("pageerror", (e) => consoleErrors.push(e.message));
  page.on("console", (m) => { if (m.type() === "error") consoleErrors.push(m.text()); });

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

    if (pathName === "/api/inventory/traceability") {
      const query = new URL(url).searchParams;
      const id = query.get("rawMessageId");
      return request.respond({
        status: 200,
        contentType: "application/json",
        // No lot filter = the balances LIST; a lot filter = that one lot's card data.
        body: JSON.stringify(id === null ? trace(listRows, []) : dataFor(id, query.get("itemId")))
      });
    }

    if (pathName === "/api/inventory/traceability/lot-card") {
      cardRequests.push(url);
      return request.respond({ status: 200, contentType: "application/pdf", body: Buffer.from("%PDF-1.4 stub") });
    }

    if (pathName === "/api/settings")
      return request.respond({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({ companyNameAr: "شركة تجريبية", companyNameEn: "Test Co", logoDataUrl: null })
      });

    return request.respond({ status: 200, contentType: "application/json", body: "[]" });
  });
}

/**
 * Opens a screen and waits until it stops navigating.
 *
 * A page that reloads while an assertion is in flight reports "Navigating frame was detached", so every
 * navigation here waits for a quiet period with no further navigation before anything is asserted.
 */
async function open(url) {
  let navigations = 0;
  const count = () => { navigations++; };
  page.on("framenavigated", count);

  await page.goto(url, { waitUntil: "load", timeout: 40000 });

  const deadline = Date.now() + 30000;
  let seen = navigations;
  let quietSince = Date.now();
  while (Date.now() < deadline) {
    await new Promise((r) => setTimeout(r, 400));
    if (navigations !== seen) {
      seen = navigations;
      quietSince = Date.now();
    } else if (Date.now() - quietSince > 1500) break;
  }

  page.off("framenavigated", count);
}

async function partA() {
  console.log("\nPART A - THE BATCH DOCUMENT (one independent card per lot)");

  await open(`${BASE}/print/stock-traceability/raw?lots=${tokenOf(LOT_A, ITEM_1)},${tokenOf(LOT_B, ITEM_1)}`);
  await page.waitForFunction(() => document.querySelectorAll(".paper").length === 2, { timeout: 20000 });

  let doc = await readSheets();
  check(doc.count === 2, "two lots print as two cards", `cards: ${doc.count}`);
  check(doc.text[0].includes(CODE_A) && doc.text[1].includes(CODE_B), "each card carries its OWN lot number");
  check(
    doc.text[0].includes("PRD-1") && !doc.text[0].includes(CODE_B) && !doc.text[1].includes(CODE_A),
    "a card contains only its own lot's movements"
  );
  check(
    doc.breakAfter.slice(0, -1).every((b) => b === "page") && doc.breakAfter.at(-1) !== "page",
    "every card but the last starts a new sheet",
    JSON.stringify(doc.breakAfter)
  );
  check(doc.toolbarButtons === 1, "the batch has ONE toolbar, not one per card", `toolbars: ${doc.toolbarButtons}`);

  console.log("\n  PDF DOWNLOAD - each card asks for its own lot, item included");
  await page.evaluate(() => {
    const buttons = Array.from(document.querySelectorAll(".no-print button"))
      .filter((b) => b.textContent.includes("Download PDF"));
    buttons[1].click(); // the SECOND card = lot B
  });
  await new Promise((r) => setTimeout(r, 1500));
  const cardUrl = cardRequests.at(-1) || "";
  const cardQuery = new URL(cardUrl || "http://x/").searchParams;
  check(
    new URL(cardUrl || "http://x/").pathname === "/api/inventory/traceability/lot-card" &&
      cardQuery.get("scope") === "raw" && cardQuery.get("rawMessageId") === LOT_B &&
      cardQuery.get("itemId") === ITEM_1,
    "the card's PDF button requests THAT lot (message AND item) on the existing lot-card endpoint",
    cardUrl
  );

  console.log("\n  TWO ITEMS OF ONE MESSAGE - two independent balances, two cards");
  await open(`${BASE}/print/stock-traceability/raw?lots=${tokenOf(LOT_A, ITEM_1)},${tokenOf(LOT_D, ITEM_2)}`);
  await page.waitForFunction(() => document.querySelectorAll(".paper").length === 2, { timeout: 20000 });
  doc = await readSheets();
  check(
    doc.count === 2,
    "the two items of one receipt message print as TWO cards, not one",
    `cards: ${doc.count}`
  );
  check(
    doc.text[0].includes("IT-1") && doc.text[0].includes("Fabric") && !doc.text[0].includes("Voile") &&
      doc.text[1].includes("IT-2") && doc.text[1].includes("Voile") && !doc.text[1].includes("Fabric"),
    "each of those cards names its own item and none of the other's",
    JSON.stringify(doc.text.map((t) => t.slice(0, 80)))
  );
  check(
    doc.text[1].includes("40") && !doc.text[1].includes("600") && !doc.text[1].includes("300"),
    "the second item's card carries its own stock, not its sibling's",
    doc.text[1].slice(0, 160)
  );

  console.log("\n  EDGE CASES - repeats, an empty choice, an unresolvable lot, an ambiguous link");
  await open(`${BASE}/print/stock-traceability/raw?lots=${tokenOf(LOT_A, ITEM_1)},${tokenOf(LOT_A, ITEM_1)},${tokenOf(LOT_B, ITEM_1)}`);
  await page.waitForFunction(() => document.querySelectorAll(".paper").length === 2, { timeout: 20000 });
  doc = await readSheets();
  check(
    doc.count === 2 && doc.text.filter((t) => t.includes(CODE_A)).length === 1,
    "the same lot listed twice still prints one card",
    `cards: ${doc.count}`
  );

  await open(`${BASE}/print/stock-traceability/raw?lots=`);
  await page.waitForFunction(() => document.body.textContent.includes("No lots were selected"), { timeout: 20000 });
  const empty = await page.evaluate(() => ({
    sheets: document.querySelectorAll(".paper").length,
    message: document.body.textContent.includes("No lots were selected for printing")
  }));
  check(empty.sheets === 0 && empty.message, "no selection prints a clear message and no card at all", JSON.stringify(empty));

  await open(`${BASE}/print/stock-traceability/raw?lots=${tokenOf(LOT_A, ITEM_1)},${tokenOf(LOT_C, ITEM_1)}`);
  await page.waitForFunction(() => document.querySelectorAll(".paper").length === 1, { timeout: 20000 });
  const mixed = await page.evaluate(() => ({
    sheets: document.querySelectorAll(".paper").length,
    goodLot: document.body.textContent.includes("MSG-2026-000001"),
    notice: document.body.textContent.includes("No lot matches this number")
  }));
  check(
    mixed.sheets === 1 && mixed.goodLot && mixed.notice,
    "an unresolvable lot fails in its own place while the other card still prints",
    JSON.stringify(mixed)
  );

  // A link that names only the message: two items answer to it, so the screen must refuse rather than
  // draw a card for whichever item came first.
  await open(`${BASE}/print/stock-traceability/raw?lots=${LOT_A}`);
  await page.waitForFunction(
    () => document.body.textContent.includes("more than one independent lot"),
    { timeout: 20000 }
  );
  const ambiguous = await page.evaluate(() => ({
    sheets: document.querySelectorAll(".paper").length,
    notice: document.body.textContent.includes("more than one independent lot"),
    claimedItem: document.body.textContent.includes("Fabric")
  }));
  check(
    ambiguous.sheets === 0 && ambiguous.notice && !ambiguous.claimedItem,
    "a message-only link prints NO card and says why, instead of printing the first item",
    JSON.stringify(ambiguous)
  );

  console.log("\n  KG AND METER - a unit the lot never recorded does not print");
  await open(`${BASE}/print/stock-traceability/raw?lots=${tokenOf(LOT_A, ITEM_1)},${tokenOf(LOT_B, ITEM_1)}`);
  await page.waitForFunction(() => document.querySelectorAll(".paper").length === 2, { timeout: 20000 });
  doc = await readSheets();
  check(doc.text[0].includes("Closing balance M"), "a lot that recorded metres closes with a metre total");
  check(doc.text[1].includes("Closing balance KG"), "the other lot still closes with its KG total");
  check(!doc.text[1].includes("Closing balance M"), "a lot that never recorded a metre prints no metre figure");

  console.log("\n  NO WRITES FROM PRINTING");
  check(writes.length === 0, "printing a batch or downloading a card sent no write to the API", JSON.stringify(writes));
  check(consoleErrors.length === 0, "no page/console errors on the card screens", JSON.stringify(consoleErrors.slice(0, 3)));
}

async function partB() {
  console.log("\nPART B - THE BALANCES SCREEN (choosing the lots, then printing them)");
  try {
    await freshPage();
    await open(`${BASE}/warehouse`);
    await page.waitForFunction(() => !!document.querySelector("table tbody tr"), { timeout: 20000 });

    const rowCheckboxes = await page.$$("table tbody input[type=checkbox]");
    check(rowCheckboxes.length === 4, "the list offers a checkbox per lot", `found ${rowCheckboxes.length}`);

    const printButtonState = () =>
      page.evaluate(() => {
        const button = Array.from(document.querySelectorAll("button"))
          .find((b) => b.textContent.includes("Print selected cards"));
        return button ? { text: button.textContent.trim(), disabled: button.disabled } : null;
      });

    const before = await printButtonState();
    check(!!before && before.disabled, "printing is refused while nothing is ticked", JSON.stringify(before));
    check(!!before && before.text.includes("(0)"), "the action shows how many cards it will print", before && before.text);

    // The two items of the SAME receipt message: two lots, so two independent ticks and two cards.
    await rowCheckboxes[0].click();
    await rowCheckboxes[1].click();
    await page.waitForFunction(() => {
      const b = Array.from(document.querySelectorAll("button")).find((x) => x.textContent.includes("Print selected cards"));
      return b && b.textContent.includes("(2)");
    }, { timeout: 10000 });

    const after = await printButtonState();
    check(!after.disabled && after.text.includes("(2)"), "ticking the two items of one message arms TWO cards", after.text);

    await page.evaluate(() => {
      Array.from(document.querySelectorAll("button"))
        .find((b) => b.textContent.includes("Print selected cards"))
        .click();
    });
    await page.waitForFunction(() => document.querySelectorAll(".paper").length === 2, { timeout: 20000 });
    check(
      page.url().includes(`/print/stock-traceability/raw?lots=${tokenOf(LOT_A, ITEM_1)},${tokenOf(LOT_D, ITEM_2)}`),
      "the batch opens the card document for exactly the ticked lots, each with its own item",
      page.url()
    );
  } catch (error) {
    // A browser that died on this screen (see the fontconfig note above) is an environment limit, not a
    // pass: it is reported as SKIPPED, and never as a check that succeeded.
    const reachable = !/detached Frame|Connection closed|Target closed|Navigating frame/.test(error.message);
    const reason = reachable ? error.message : `the browser cannot render this screen in this environment (${error.message.slice(0, 60)})`;
    skip("the balances screen: tick lots -> print their cards", reason);
    if (!reachable) {
      skip("a checkbox per lot, the disabled/count state of the print action", reason);
      skip("the screen opening the card document for exactly the ticked lots", reason);
    } else {
      failures.push(`the balances screen check threw: ${error.message}`);
    }
  }
}

(async () => {
  try {
    await launch();
    bindStubs();

    // The document first: it is the half this environment can render reliably, so a browser that dies
    // on the balances screen can never take the proven checks down with it.
    await partA();
    await partB();
  } catch (error) {
    failures.push(`the check threw: ${error.message}`);
    console.log(`\n  FAIL  the check threw: ${error.message}`);
  } finally {
    await browser?.close().catch(() => {});
  }

  console.log(`\nfailures: ${failures.length}`);
  if (failures.length) console.log(failures.map((f) => ` - ${f}`).join("\n"));
  if (skipped.length) {
    console.log(`skipped (NOT verified here): ${skipped.length}`);
    console.log(skipped.map((s) => ` - ${s}`).join("\n"));
  }
  process.exit(failures.length === 0 ? 0 : 1);
})();
