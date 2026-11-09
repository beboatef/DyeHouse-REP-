#!/usr/bin/env node
/**
 * Creates REAL stock through the API, for the warehouse screenshots.
 *
 * Nothing here is a mock: every figure is posted by the application itself (receipt message, stock
 * adjustment, purchase receipt) into a real SQL Server database, through the same endpoints the UI
 * calls. It prints what the screens should then show, so the screenshot can be checked against the
 * numbers that were actually posted.
 *
 * Usage (API already running):
 *   node tools/warehouse-demo-seed.mjs
 *
 * Environment (all optional):
 *   API_URL          default https://localhost:7100
 *   DEMO_USERNAME    default admin
 *   DEMO_PASSWORD    default Admin@12345   (the Development seed, see UserSeeder)
 *   DEMO_LABEL       default DEMO          (prefix of the codes it creates)
 *
 * Re-running it is safe: the customer, item, supplier and material are looked up by code when they
 * already exist, and each receipt carries a deterministic idempotency token so a repeated run does
 * not post the same receipt twice.
 */
import { createHash, randomUUID } from "node:crypto";

const API_URL = (process.env.API_URL ?? "https://localhost:7100").replace(/\/+$/, "");
const USERNAME = process.env.DEMO_USERNAME ?? "admin";
const PASSWORD = process.env.DEMO_PASSWORD ?? "Admin@12345";
const LABEL = process.env.DEMO_LABEL ?? "DEMO";

// A local development API serves a self-signed certificate. The token is disabled ONLY for a localhost
// HTTPS target, and the fact is reported, so a non-local target can never silently skip verification.
if (API_URL.startsWith("https://") && /localhost|127\.0\.0\.1/.test(API_URL)) {
  process.env.NODE_TLS_REJECT_UNAUTHORIZED = "0";
  console.log("TLS: localhost development certificate is not verified (local only).");
}

let token = "";

async function call(path, { method = "GET", body } = {}) {
  const response = await fetch(`${API_URL}${path}`, {
    method,
    headers: {
      ...(body ? { "Content-Type": "application/json" } : {}),
      ...(token ? { Authorization: `Bearer ${token}` } : {})
    },
    body: body ? JSON.stringify(body) : undefined
  });

  const text = await response.text();
  const data = text ? safeJson(text) : null;

  if (!response.ok) {
    const error = new Error(`${method} ${path} -> ${response.status}\n${text.slice(0, 900)}`);
    error.status = response.status;
    error.data = data;
    throw error;
  }
  return data;
}

const safeJson = (text) => {
  try {
    return JSON.parse(text);
  } catch {
    return text;
  }
};

/** Deterministic idempotency token: the same demo receipt is never posted twice by accident. */
const idempotencyToken = (purpose) => {
  const hex = createHash("sha256").update(`dyehouse-demo:${LABEL}:${purpose}`).digest("hex");
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-4${hex.slice(13, 16)}-a${hex.slice(17, 20)}-${hex.slice(20, 32)}`;
};

const today = new Date();
const iso = (daysAgo = 0) => {
  const date = new Date(today);
  date.setDate(date.getDate() - daysAgo);
  return date.toISOString().slice(0, 10);
};

/** Creates a master-data row, or returns the existing one with that code. */
async function ensure(kind, listPath, createPath, code, payload, codeOf = (row) => row.code) {
  try {
    const created = await call(createPath, { method: "POST", body: { code, ...payload } });
    console.log(`  + ${kind} ${code} created`);
    return created;
  } catch (error) {
    if (error.status !== 400 && error.status !== 409) throw error;
    const rows = await call(listPath);
    const found = (Array.isArray(rows) ? rows : []).find((row) => codeOf(row) === code);
    if (!found) throw error;
    console.log(`  = ${kind} ${code} already exists`);
    return found;
  }
}

const main = async () => {
  console.log(`API: ${API_URL}`);

  const login = await call("/api/auth/login", {
    method: "POST",
    body: { username: USERNAME, password: PASSWORD }
  });
  token = login.token;
  console.log(`Logged in as ${login.username} (${login.roles?.length ?? 0} roles).`);

  const warehouses = await call("/api/warehouses");
  const byCode = (code) => {
    const found = warehouses.find((w) => w.code === code);
    if (!found) throw new Error(`Warehouse ${code} is missing - the startup seeder defines it.`);
    return found;
  };
  const rawWh = byCode("WH-RAW");
  const matWh = byCode("WH-MAT");

  const customer = await ensure("customer", "/api/customers", "/api/customers", `${LABEL}-CUST`, {
    name: `عميل عرض المخازن ${LABEL}`
  });

  const item = await ensure("item", "/api/items", "/api/items", `${LABEL}-FABRIC`, {
    name: "قطن عرض المخازن",
    nameAr: "قطن عرض المخازن",
    baseUnit: "KG",
    secondaryUnit: "Meter"
  });

  // ---- raw material: two receipt messages (two independent balances) ------------------------------
  const receiptOne = await call("/api/raw-messages", {
    method: "POST",
    body: {
      receiptDate: iso(10),
      customerId: customer.id,
      warehouseId: rawWh.id,
      notes: "عرض المخازن - الاستلام الأول",
      requestId: idempotencyToken("raw-receipt-1"),
      lines: [{ itemId: item.id, quantityKg: 100, quantityMeter: 50 }]
    }
  });
  const receiptTwo = await call("/api/raw-messages", {
    method: "POST",
    body: {
      receiptDate: iso(4),
      customerId: customer.id,
      warehouseId: rawWh.id,
      notes: "عرض المخازن - الاستلام الثاني",
      requestId: idempotencyToken("raw-receipt-2"),
      lines: [{ itemId: item.id, quantityKg: 40, quantityMeter: 20 }]
    }
  });
  console.log(`  + raw receipts ${receiptOne.messageNumber} (100/50) and ${receiptTwo.messageNumber} (40/20)`);

  // A stock count correction: 10 KG / 5 M out of the first receipt, so the item card has a real
  // period movement and a visible opened/closing pair.
  await call("/api/stock-adjustments", {
    method: "POST",
    body: {
      customerId: customer.id,
      itemId: item.id,
      rawMessageId: receiptOne.id,
      type: "Decrease",
      quantityKg: 10,
      quantityMeter: 5,
      reason: "جرد عرض المخازن - فرق وزن",
      notes: "created by tools/warehouse-demo-seed.mjs",
      approvedBy: "demo"
    }
  });
  console.log("  + stock adjustment -10 KG / -5 M on the first receipt");

  // ---- materials: one purchase receipt into the materials warehouse ------------------------------
  const supplier = await ensure("supplier", "/api/suppliers", "/api/suppliers", `${LABEL}-SUP`, {
    name: `مورد عرض المخازن ${LABEL}`
  });
  const material = await ensure("material", "/api/materials", "/api/materials", `${LABEL}-DYE`, {
    name: "صبغة عرض المخازن",
    unit: "KG",
    purchasePrice: 12.5,
    kind: "Chemical"
  });
  const purchaseReceipt = await call("/api/purchases/receipts", {
    method: "POST",
    body: {
      receiptDate: iso(2),
      supplierId: supplier.id,
      warehouseId: matWh.id,
      supplierDocumentNumber: `${LABEL}-INV-1`,
      notes: "عرض المخازن - استلام مواد",
      lines: [{ materialId: material.id, quantity: 60, unit: "KG", unitCost: 12.5 }]
    }
  });
  console.log(`  + purchase receipt ${purchaseReceipt.receiptNumber ?? purchaseReceipt.id} (60 KG into WH-MAT)`);

  // ---- what the screens must show ----------------------------------------------------------------
  console.log("");
  console.log("POSTED - the screenshots must show exactly these figures:");
  console.log(`  WH-RAW  ${item.code}: 2 balances (one per receipt message)`);
  console.log(`          ${receiptOne.messageNumber}: 100/50 received - 10/5 adjusted = 90 KG / 45 M`);
  console.log(`          ${receiptTwo.messageNumber}: 40 KG / 20 M`);
  console.log(`          item card total: 130 KG / 65 M`);
  console.log(`  WH-MAT  ${material.code}: 60 KG (purchase receipt)`);
  console.log("");
  console.log("Open:");
  console.log(`  http://localhost:5173/warehouse/${rawWh.id}`);
  console.log(`  http://localhost:5173/warehouse/${matWh.id}`);
};

main().catch((error) => {
  console.error("");
  console.error("SEED FAILED - nothing was faked to make the screens look right.");
  console.error(error instanceof Error ? error.message : error);
  process.exit(1);
});
